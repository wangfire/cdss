using HospitalAi.Application.Coding.Preprocessing;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HospitalAi.Tools;

/// <summary>
/// 数据回填。支持 dry-run / apply / verify 三种模式，永不删除数据：
///
/// 1) 文档回填：把现有 medical_document 作为初始版本，沿用已有 version 与 content_hash，
///    补齐 document_section 切片并记录解析版本；没有内容的文书只标记 HUMAN_REQUIRED。
/// 2) Legacy 推荐标记：非 V2.2 的编码任务与推荐统一标记 phase2-mvp-legacy + LEGACY_READ_ONLY，
///    只读保留，Final Coding 与人工审核记录数量不变。
///
/// 校验：
///    源 medical_document 数量 = 回填文档版本数量
///    每个文档 content_hash 唯一；每个文档至少一个 Chunk 或明确 HUMAN_REQUIRED
///    原 coding_recommendation 总数 = Legacy 标记后数量
///    Final Coding 数量不变；人工审核记录数量不变
/// </summary>
public static class BackfillCommand
{
    private const string LegacyPipelineVersion = PipelineVersions.Legacy;
    private const string ParseVersion = "v22-lite-backfill-v1";

    public static async Task<int> RunAsync(IConfiguration configuration)
    {
        var mode = (configuration["Mode"] ?? "dry-run").ToLowerInvariant();
        if (mode is not ("dry-run" or "apply" or "verify"))
        {
            Console.Error.WriteLine("--mode 仅支持 dry-run / apply / verify。");
            return 2;
        }

        var connectionString = ToolHost.ResolveConnectionString(configuration);
        var hospitalId = ToolHost.ResolveHospitalId(configuration);
        var options = new DbContextOptionsBuilder<HospitalAiDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        ToolHost.Print($"回填模式：{mode}");
        ToolHost.Print($"目标库：{MaskConnectionString(connectionString)}");
        if (hospitalId is { } scopedHospital)
        {
            ToolHost.Print($"限定医院：{scopedHospital}");
        }

        await using var context = new HospitalAiDbContext(options);

        var before = await CaptureAsync(context, hospitalId, CancellationToken.None);
        ToolHost.Print($"回填前：文书 {before.Documents}，当前版本 {before.CurrentDocuments}，"
            + $"无切片文书 {before.DocumentsWithoutChunk}，切片 {before.Sections}，"
            + $"任务 {before.Tasks}，推荐 {before.Recommendations}，"
            + $"Final Coding {before.FinalCodings}，人工审核 {before.Reviews}");

        if (mode == "verify")
        {
            return Report(before, before);
        }

        var applied = mode == "apply";
        var result = await RunAsync(context, hospitalId, apply: applied, CancellationToken.None);

        ToolHost.Print(
            $"{(applied ? "已回填" : "预演（未写入）")}："
            + $"补齐切片文书 {result.DocumentsChunked}，新增切片 {result.SectionsAdded}，"
            + $"标记 HUMAN_REQUIRED 文书 {result.DocumentsHumanRequired}，"
            + $"标记 Legacy 推荐 {result.RecommendationsMarked}，"
            + $"标记 Legacy 任务 {result.TasksMarked}。");

        var after = await CaptureAsync(context, hospitalId, CancellationToken.None);
        return Report(before, after);
    }

    private static async Task<BackfillResult> RunAsync(
        HospitalAiDbContext context,
        Guid? hospitalId,
        bool apply,
        CancellationToken cancellationToken)
    {
        var result = new BackfillResult();
        var now = DateTimeOffset.UtcNow;

        // 1) 文档回填：现有文书即初始版本，沿用 version / content_hash，只补切片。
        var documents = await context.MedicalDocuments
            .Where(item => hospitalId == null || item.HospitalId == hospitalId)
            .ToListAsync(cancellationToken);
        var sectionCounts = await context.DocumentSections
            .Where(item => hospitalId == null || item.HospitalId == hospitalId)
            .GroupBy(item => item.MedicalDocumentId)
            .Select(group => new { MedicalDocumentId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var chunkCountByDocument = sectionCounts
            .ToDictionary(item => item.MedicalDocumentId, item => item.Count);

        foreach (var document in documents)
        {
            if (chunkCountByDocument.TryGetValue(document.Id, out var existing) && existing > 0)
            {
                continue;
            }

            var chunks = DocumentChunker.Chunk(document.ContentReference ?? string.Empty);
            if (chunks.Count == 0)
            {
                // 内容为空：不伪造切片，明确转人工，并保留原文引用位置不变。
                result.DocumentsHumanRequired++;
                if (apply)
                {
                    document.DocumentStatus = "HUMAN_REQUIRED";
                    document.UpdatedAt = now;
                }

                continue;
            }

            result.DocumentsChunked++;
            result.SectionsAdded += chunks.Count;
            if (!apply)
            {
                continue;
            }

            var sequence = existing;
            foreach (var chunk in chunks)
            {
                sequence++;
                context.DocumentSections.Add(new DocumentSectionRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = document.HospitalId,
                    VisitId = document.VisitId,
                    MedicalDocumentId = document.Id,
                    SectionType = "CHUNK",
                    Title = document.DocumentType,
                    Content = chunk.Text,
                    Sequence = sequence,
                    StartPosition = chunk.StartPosition,
                    EndPosition = chunk.EndPosition,
                    TokenCount = EstimateTokens(chunk.Text),
                    ContentHash = chunk.ContentHash,
                    EmbeddingStatus = "UNAVAILABLE",
                    IndexStatus = "INDEXED",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            // 解析版本只记录，不覆盖原文引用。
            document.ParseVersion ??= ParseVersion;
            document.UpdatedAt = now;
        }

        // 2) Legacy 推荐标记：不删除、不重写，只改标记。
        var legacyRecommendations = await context.CodingRecommendations
            .Where(item => hospitalId == null || item.HospitalId == hospitalId)
            .Where(item => item.PipelineVersion == null
                || item.PipelineVersion == string.Empty
                || (item.PipelineVersion != PipelineVersions.Lite
                    && item.PipelineVersion != PipelineVersions.Full))
            .ToListAsync(cancellationToken);

        var legacyTasks = await context.CodingTasks
            .Where(item => hospitalId == null || item.HospitalId == hospitalId)
            .Where(item => item.PipelineVersion == null
                || item.PipelineVersion == string.Empty
                || (item.PipelineVersion != PipelineVersions.Lite
                    && item.PipelineVersion != PipelineVersions.Full))
            .ToListAsync(cancellationToken);

        result.RecommendationsMarked = legacyRecommendations.Count;
        result.TasksMarked = legacyTasks.Count;

        if (apply)
        {
            foreach (var recommendation in legacyRecommendations)
            {
                recommendation.PipelineVersion = LegacyPipelineVersion;
                // Legacy 结果只读：不得被 V2.2 重跑、审核或策略改动。
                recommendation.LifecycleStatus = "LEGACY_READ_ONLY";
            }

            foreach (var task in legacyTasks)
            {
                task.PipelineVersion = LegacyPipelineVersion;
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    private static async Task<Snapshot> CaptureAsync(
        HospitalAiDbContext context,
        Guid? hospitalId,
        CancellationToken cancellationToken)
    {
        var documents = context.MedicalDocuments.Where(item => hospitalId == null || item.HospitalId == hospitalId);
        var sections = context.DocumentSections.Where(item => hospitalId == null || item.HospitalId == hospitalId);
        var tasks = context.CodingTasks.Where(item => hospitalId == null || item.HospitalId == hospitalId);
        var recommendations = context.CodingRecommendations
            .Where(item => hospitalId == null || item.HospitalId == hospitalId);
        var finals = context.FinalCodingResults.Where(item => hospitalId == null || item.HospitalId == hospitalId);
        var reviews = context.CodingReviews.Where(item => hospitalId == null || item.HospitalId == hospitalId);

        var documentList = await documents
            .Select(item => new { item.Id, item.ContentHash, item.DocumentStatus })
            .ToListAsync(cancellationToken);
        var documentIds = documentList.Select(item => item.Id).ToList();
        var chunkCounts = await sections
            .Where(item => documentIds.Contains(item.MedicalDocumentId))
            .GroupBy(item => item.MedicalDocumentId)
            .Select(group => new { MedicalDocumentId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.MedicalDocumentId, item => item.Count, cancellationToken);

        return new Snapshot(
            Documents: documentList.Count,
            CurrentDocuments: documentList.Count(item => item.DocumentStatus != "HUMAN_REQUIRED"),
            DocumentsHumanRequired: documentList.Count(item => item.DocumentStatus == "HUMAN_REQUIRED"),
            DocumentsWithoutChunk: documentList.Count(
                item => item.DocumentStatus != "HUMAN_REQUIRED"
                    && (!chunkCounts.TryGetValue(item.Id, out var count) || count == 0)),
            DuplicateContentHashes: documentList
                .GroupBy(item => item.ContentHash)
                .Count(group => group.Count() > 1),
            Sections: await sections.CountAsync(cancellationToken),
            Tasks: await tasks.CountAsync(cancellationToken),
            Recommendations: await recommendations.CountAsync(cancellationToken),
            FinalCodings: await finals.CountAsync(cancellationToken),
            Reviews: await reviews.CountAsync(cancellationToken));
    }

    private static int Report(Snapshot before, Snapshot after)
    {
        var failures = new List<string>();

        // 源文书数量 = 回填后仍视为版本的文书数量（HUMAN_REQUIRED 是显式例外，不当作版本）。
        if (before.Documents != after.CurrentDocuments + after.DocumentsHumanRequired)
        {
            failures.Add(
                $"文书数量不守恒：源 {before.Documents} != 版本 {after.CurrentDocuments} + 待人工 {after.DocumentsHumanRequired}");
        }

        if (after.DocumentsWithoutChunk > 0)
        {
            failures.Add($"{after.DocumentsWithoutChunk} 个文书既没有切片也没有标记 HUMAN_REQUIRED");
        }

        if (after.DuplicateContentHashes > 0)
        {
            failures.Add($"存在 {after.DuplicateContentHashes} 组重复 content_hash");
        }

        if (before.Recommendations != after.Recommendations)
        {
            failures.Add(
                $"推荐数量不守恒：{before.Recommendations} != {after.Recommendations}（回填不得增删推荐）");
        }

        if (before.FinalCodings != after.FinalCodings)
        {
            failures.Add(
                $"Final Coding 数量不守恒：{before.FinalCodings} != {after.FinalCodings}（已确认结果不得被动过）");
        }

        if (before.Reviews != after.Reviews)
        {
            failures.Add($"人工审核记录数量不守恒：{before.Reviews} != {after.Reviews}");
        }

        ToolHost.Print(
            $"回填后：文书 {after.Documents}，可用版本 {after.CurrentDocuments}，"
            + $"待人工 {after.DocumentsHumanRequired}，无切片 {after.DocumentsWithoutChunk}，"
            + $"切片 {after.Sections}，任务 {after.Tasks}，推荐 {after.Recommendations}，"
            + $"Final Coding {after.FinalCodings}，人工审核 {after.Reviews}");

        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                Console.Error.WriteLine($"校验失败：{failure}");
            }

            return 1;
        }

        ToolHost.Print("校验通过：文书、推荐、Final Coding、人工审核数量全部守恒，无重复 content_hash。");
        return 0;
    }

    private static int EstimateTokens(string text)
    {
        // 与流水线一致的粗略估算：中英文混合文本按字符数折算，禁止引入外部依赖。
        return (int)Math.Ceiling(text.Length / 2.0);
    }

    private static string MaskConnectionString(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "***";
            }

            return builder.ToString();
        }
        catch (ArgumentException)
        {
            return "***";
        }
    }

    private sealed record Snapshot(
        int Documents,
        int CurrentDocuments,
        int DocumentsHumanRequired,
        int DocumentsWithoutChunk,
        int DuplicateContentHashes,
        int Sections,
        int Tasks,
        int Recommendations,
        int FinalCodings,
        int Reviews);

    private sealed class BackfillResult
    {
        public int DocumentsChunked { get; set; }
        public int SectionsAdded { get; set; }
        public int DocumentsHumanRequired { get; set; }
        public int RecommendationsMarked { get; set; }
        public int TasksMarked { get; set; }
    }
}
