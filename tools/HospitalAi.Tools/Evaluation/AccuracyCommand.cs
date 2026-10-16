using System.Text.Json;
using HospitalAi.Application.Coding.Preprocessing;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker;
using HospitalAi.Worker.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HospitalAi.Tools;

/// <summary>
/// evaluate-accuracy：常见病/手术诊断名称精度评测（L3 精标语料口径，用于防退化冒烟；
/// 决策 3 的验收线归属见 docs/2026-10-08-精度验收口径规范.md §7，不能只看本表判达标）。
/// 把源医院（已导入字典/向量/规则）的知识克隆到临时医院，逐条名称真实跑一遍
/// V2.2-Lite（索引两段式 + 向量 + Rerank），计算：
///   编码级 Top1 / Top3（精确码一致）与类目级 Top1（编码首段一致，如 K35/I21/47）。
/// 词典中不存在的预期码告警并从命中率剔除；结束清理临时医院全部数据。
/// 用法：evaluate-accuracy --connection "..." --hospital &lt;源医院guid&gt;
///   [--accuracy data/phase1/golden/accuracy-dataset.json] [--limit N] [--report &lt;path&gt;]
/// </summary>
public static class AccuracyCommand
{
    private sealed record AccuracyCase(
        string CaseId,
        string Channel,
        string Name,
        string ExpectedCode,
        string? Note);

    private sealed record CaseOutcome(
        string CaseId,
        string Name,
        string ExpectedCode,
        string? Top1Code,
        bool CodingTop1Hit,
        bool CodingTop3Hit,
        bool CategoryTop1Hit,
        bool HasRecommendation,
        string Channels,
        double LatencyMs,
        bool ExpectedInDictionary);

    public static async Task<int> RunAsync(IConfiguration configuration)
    {
        var connectionString = ToolHost.ResolveConnectionString(configuration);
        var sourceHospitalId = ToolHost.ResolveHospitalId(configuration)
            ?? throw new InvalidOperationException("缺少 --hospital 源医院（需已导入字典与向量）。");
        var datasetPath = configuration["Accuracy"] ?? "data/phase1/golden/accuracy-dataset.json";
        var limit = int.TryParse(configuration["limit"], out var parsedLimit) ? parsedLimit : 0;
        var reportPath = configuration["report"]
            ?? $"docs/superpowers/reports/accuracy-baseline-{DateTimeOffset.UtcNow:yyyyMMdd-HHmm}.json";
        if (!File.Exists(datasetPath))
        {
            Console.Error.WriteLine($"精度数据集不存在：{datasetPath}");
            return 2;
        }

        var cases = await LoadCasesAsync(datasetPath);
        if (limit > 0)
        {
            cases = cases.Take(limit).ToList();
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddDbContext<HospitalAiDbContext>(options => options.UseSqlServer(connectionString));
        services.AddCodingTaskPipeline(configuration);
        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();

        var hospitalId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        try
        {
            await SeedHospitalAsync(dbContext, hospitalId, patientId);
            Console.WriteLine($"临时评测医院 {hospitalId}，正在克隆源医院 {sourceHospitalId} 的知识…");
            await CopyKnowledgeAsync(dbContext, sourceHospitalId, hospitalId);

            var dictionaryCodes = await dbContext.MedicalCodes
                .AsNoTracking()
                .Where(item => item.HospitalId == hospitalId)
                .Select(item => item.Code)
                .ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

            var warnings = cases
                .Where(item => !dictionaryCodes.Contains(item.ExpectedCode))
                .Select(item => $"{item.CaseId} 预期码 {item.ExpectedCode} 不在词典中，已从命中率剔除（{item.Name}）")
                .ToList();
            foreach (var warning in warnings)
            {
                Console.WriteLine("  [告警] " + warning);
            }

            var evaluated = cases
                .Where(item => dictionaryCodes.Contains(item.ExpectedCode))
                .ToList();

            var results = new List<CaseOutcome>();
            foreach (var item in evaluated)
            {
                try
                {
                    results.Add(await RunCaseAsync(
                        scope.ServiceProvider,
                        dbContext,
                        hospitalId,
                        patientId,
                        item,
                        cancellationToken: CancellationToken.None));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Console.WriteLine($"  [用例失败] {item.CaseId} {item.Name}: {exception.Message}");
                    var frames = exception.StackTrace?.Split('\n').Where(line => line.Contains("HospitalAi.")).Take(4).ToList()
                        ?? [];
                    foreach (var frame in frames)
                    {
                        Console.WriteLine("    " + frame.Trim());
                    }

                    if (exception.InnerException is { } inner)
                    {
                        Console.WriteLine("    inner: " + inner.Message);
                    }

                    // 诊断辅助：打印仍处于 Modified 状态的实体（EF 抛并发异常后不落库，追踪器保留现场）。
                    foreach (var entry in dbContext.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Modified))
                    {
                        var rowVersion = entry.Properties.First(property => property.Metadata.Name == "RowVersion").CurrentValue as byte[];
                        Console.WriteLine($"    [Modified] {entry.Metadata.ClrType.Name} id={entry.Property("Id").CurrentValue} rowVersionLen={rowVersion?.Length ?? 0}");
                    }
                }
            }

            return Report(results, cases.Count, warnings, reportPath);
        }
        finally
        {
            Console.WriteLine("清理临时评测医院数据…");
            try
            {
                await CleanupAsync(dbContext, hospitalId, patientId);
            }
            catch (Exception cleanupException)
            {
                // 清理失败不得掩盖用例执行阶段的原始异常。
                Console.Error.WriteLine("清理失败（原始异常见上方）：" + cleanupException.Message);
            }
        }
    }

    private static async Task<List<AccuracyCase>> LoadCasesAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var document = await JsonDocument.ParseAsync(stream);
        var cases = new List<AccuracyCase>();
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            cases.Add(new AccuracyCase(
                item.GetProperty("caseId").GetString()!,
                item.GetProperty("channel").GetString()!,
                item.GetProperty("name").GetString()!,
                item.GetProperty("expectedCode").GetString()!,
                item.TryGetProperty("note", out var note) ? note.GetString() : null));
        }

        return cases;
    }

    private static async Task<CaseOutcome> RunCaseAsync(
        IServiceProvider serviceProvider,
        HospitalAiDbContext dbContext,
        Guid hospitalId,
        Guid patientId,
        AccuracyCase accuracyCase,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var visitId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var traceId = Guid.NewGuid().ToString("N");
        var documentText = accuracyCase.Channel == "procedure"
            ? $"【手术操作】{accuracyCase.Name}。"
            : $"【出院诊断】{accuracyCase.Name}。";

        dbContext.Visits.Add(new VisitRecord
        {
            Id = visitId,
            HospitalId = hospitalId,
            PatientId = patientId,
            AdmissionAt = now.AddDays(-2),
            DischargeAt = now.AddDays(-1),
            CreatedAt = now,
            UpdatedAt = now
        });
        dbContext.MedicalDocuments.Add(new MedicalDocumentRecord
        {
            Id = documentId,
            HospitalId = hospitalId,
            VisitId = visitId,
            DocumentType = "ACCURACY_CASE",
            ContentReference = documentText,
            ContentHash = DocumentChunker.ComputeHash(documentText),
            Version = 1,
            DocumentStatus = "ACTIVE",
            IsCurrent = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        dbContext.CodingTasks.Add(new CodingTaskRecord
        {
            Id = taskId,
            HospitalId = hospitalId,
            VisitId = visitId,
            PipelineVersion = PipelineVersions.Lite,
            // 以 Running 播种：评测进程自己就是执行方。若播 Pending，正在运行的
            // API 进程内派发器会原子认领该任务（状态更新顺带改 rowversion），
            // 与本进程的 Runner 更新撞乐观并发（WP-B2-3 认领机制的正确副作用）。
            Status = CodingTaskStatus.Running,
            CodingStage = CodingStage.Imported,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        dbContext.PipelineTraces.Add(new PipelineTraceRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            CodingTaskId = taskId,
            TraceId = traceId,
            Status = "PENDING",
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var runner = serviceProvider.GetRequiredService<V22LiteCodingPipelineRunner>();
        var startedAt = DateTimeOffset.UtcNow;
        await runner.RunAsync(
            new CodingTaskCreatedMessage(
                Guid.NewGuid(),
                hospitalId,
                taskId,
                visitId,
                PipelineVersions.Lite,
                traceId,
                Guid.NewGuid().ToString("N")),
            cancellationToken);
        var latencyMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

        var recommendations = await dbContext.CodingRecommendations
            .AsNoTracking()
            .Where(item => item.CodingTaskId == taskId)
            .OrderBy(item => item.Rank)
            .Select(item => new { item.Code, item.RecallChannel })
            .ToListAsync(cancellationToken);

        var top1 = recommendations.FirstOrDefault();
        var top3 = recommendations.Take(3).Select(item => item.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expected = accuracyCase.ExpectedCode;
        var expectedCategory = CategoryOf(expected);
        var top1Code = top1?.Code;

        return new CaseOutcome(
            accuracyCase.CaseId,
            accuracyCase.Name,
            expected,
            top1Code,
            top1Code is not null && CodingHit(top1Code, expected),
            top3.Any(code => CodingHit(code, expected)),
            top1Code is not null && CategoryOf(top1Code) == expectedCategory,
            recommendations.Count > 0,
            string.Join("/", recommendations.Take(3).Select(item => item.RecallChannel)),
            latencyMs,
            ExpectedInDictionary: true);
    }

    // 编码级命中：精确一致，或实际码以预期码为前缀（同码家族的扩展位，如 K35.900 与 K35.900x001）。
    private static bool CodingHit(string actual, string expected)
    {
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            || actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string CategoryOf(string code)
    {
        var dot = code.IndexOf('.');
        return dot > 0 ? code[..dot] : code;
    }

    private static async Task SeedHospitalAsync(
        HospitalAiDbContext dbContext,
        Guid hospitalId,
        Guid patientId)
    {
        var now = DateTimeOffset.UtcNow;
        dbContext.Hospitals.Add(new HospitalRecord
        {
            Id = hospitalId,
            Code = $"ACC-{hospitalId:N}"[..12],
            Name = "精度评测临时医院",
            Status = "ACTIVE",
            CreatedAt = now,
            UpdatedAt = now
        });
        dbContext.Patients.Add(new PatientRecord
        {
            Id = patientId,
            HospitalId = hospitalId,
            SourceSystem = "ACCURACY",
            SourcePatientId = patientId.ToString("N"),
            DisplayName = null,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
    }

    /// <summary>把源医院的知识（编码体系/字典/向量/同义词/规则）克隆到临时医院。</summary>
    private static async Task CopyKnowledgeAsync(
        HospitalAiDbContext dbContext,
        Guid sourceHospitalId,
        Guid targetHospitalId)
    {
        var now = DateTimeOffset.UtcNow;

        // 1) 编码体系（medical_code.code_system_id 有外键，需重映射主键）。
        var sourceSystems = await dbContext.CodeSystems
            .AsNoTracking()
            .Where(item => item.HospitalId == sourceHospitalId)
            .ToListAsync();
        var systemIdMap = new Dictionary<Guid, Guid>();
        foreach (var system in sourceSystems)
        {
            var newId = Guid.NewGuid();
            systemIdMap[system.Id] = newId;
            dbContext.CodeSystems.Add(new CodeSystemRecord
            {
                Id = newId,
                HospitalId = targetHospitalId,
                Code = system.Code,
                Name = system.Name,
                Version = system.Version,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await dbContext.SaveChangesAsync();

        // 2) 医学编码字典。
        var sourceCodes = await dbContext.MedicalCodes
            .AsNoTracking()
            .Where(item => item.HospitalId == sourceHospitalId)
            .ToListAsync();
        foreach (var batch in sourceCodes.Chunk(2000))
        {
            dbContext.MedicalCodes.AddRange(batch.Select(item => new MedicalCodeRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = targetHospitalId,
                CodeSystemId = systemIdMap[item.CodeSystemId],
                CodeSystemCode = item.CodeSystemCode,
                Code = item.Code,
                Title = item.Title,
                CodeType = item.CodeType,
                SearchText = item.SearchText,
                IsEnabled = item.IsEnabled,
                CreatedAt = now,
                UpdatedAt = now
            }));
            await dbContext.SaveChangesAsync();
        }

        Console.WriteLine($"  字典已克隆：{sourceCodes.Count} 条");

        // 3) 标题向量。
        var sourceEmbeddings = await dbContext.MedicalCodeEmbeddings
            .AsNoTracking()
            .Where(item => item.HospitalId == sourceHospitalId)
            .ToListAsync();
        foreach (var batch in sourceEmbeddings.Chunk(500))
        {
            dbContext.MedicalCodeEmbeddings.AddRange(batch.Select(item => new MedicalCodeEmbeddingRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = targetHospitalId,
                CodeSystemCode = item.CodeSystemCode,
                Code = item.Code,
                ModelCode = item.ModelCode,
                Title = item.Title,
                Dim = item.Dim,
                Vector = item.Vector,
                CreatedAt = now,
                UpdatedAt = now
            }));
            await dbContext.SaveChangesAsync();
        }

        Console.WriteLine($"  向量已克隆：{sourceEmbeddings.Count} 条");

        // 4) 字典同义词（医院级 term_synonym；索引链路词库 icd_term_synonym 为平台级共享，无需克隆）。
        var sourceSynonyms = await dbContext.TermSynonyms
            .AsNoTracking()
            .Where(item => item.HospitalId == sourceHospitalId)
            .ToListAsync();
        foreach (var batch in sourceSynonyms.Chunk(2000))
        {
            dbContext.TermSynonyms.AddRange(batch.Select(item => new TermSynonymRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = targetHospitalId,
                Term = item.Term,
                NormalizedTerm = item.NormalizedTerm,
                CodeSystemCode = item.CodeSystemCode,
                Code = item.Code,
                EntityType = item.EntityType,
                CreatedAt = now,
                UpdatedAt = now
            }));
            await dbContext.SaveChangesAsync();
        }

        // 5) 编码规则。
        var sourceRules = await dbContext.CodingRules
            .AsNoTracking()
            .Where(item => item.HospitalId == sourceHospitalId)
            .ToListAsync();
        dbContext.CodingRules.AddRange(sourceRules.Select(item => new CodingRuleRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = targetHospitalId,
            RuleCode = item.RuleCode,
            RuleVersion = item.RuleVersion,
            CodeSystemCode = item.CodeSystemCode,
            CodePattern = item.CodePattern,
            RuleType = item.RuleType,
            Severity = item.Severity,
            Message = item.Message,
            Priority = item.Priority,
            RuleGroup = item.RuleGroup,
            ConditionJson = item.ConditionJson,
            ActionJson = item.ActionJson,
            Blocking = item.Blocking,
            IsBuiltin = item.IsBuiltin,
            IsEnabled = item.IsEnabled,
            CreatedAt = now,
            UpdatedAt = now
        }));
        await dbContext.SaveChangesAsync();
        Console.WriteLine($"  同义词 {sourceSynonyms.Count} 条、规则 {sourceRules.Count} 条已克隆");
    }

    private static int Report(
        IReadOnlyList<CaseOutcome> results,
        int totalCases,
        IReadOnlyList<string> warnings,
        string reportPath)
    {
        var total = results.Count;
        var codingTop1 = results.Count(item => item.CodingTop1Hit);
        var codingTop3 = results.Count(item => item.CodingTop3Hit);
        var categoryTop1 = results.Count(item => item.CategoryTop1Hit);
        var covered = results.Count(item => item.HasRecommendation);
        var procedures = results.Where(item => item.Name.EndsWith("术") || item.Name.Contains("切除") || item.Name.Contains("引流") || item.Name.Contains("碎石")).ToList();

        static string Percent(int part, int total) =>
            total == 0 ? "n/a" : $"{Math.Round(100.0 * part / total, 1)}%";

        Console.WriteLine();
        Console.WriteLine("==== 精度评测报告（V2.2-Lite：索引两段式 + 向量 + Rerank）====");
        Console.WriteLine($"用例             {total}/{totalCases}（{warnings.Count} 条预期码不在词典已剔除）");
        Console.WriteLine($"编码级 Top1      {codingTop1}/{total} = {Percent(codingTop1, total)}   （验收线 ≥25%）");
        Console.WriteLine($"编码级 Top3      {codingTop3}/{total} = {Percent(codingTop3, total)}");
        Console.WriteLine($"类目级 Top1      {categoryTop1}/{total} = {Percent(categoryTop1, total)}   （验收线 ≥50%）");
        Console.WriteLine($"出码率           {covered}/{total} = {Percent(covered, total)}");
        Console.WriteLine($"平均耗时         {(total == 0 ? 0 : results.Average(item => item.LatencyMs)):F0} ms/例");
        Console.WriteLine(
            "口径说明         本表是 L3 精标语料口径（小规模人工构造，样本量小且经人工挑选），只用于防退化冒烟；" +
            "决策 3 的验收线挂在哪一层见 docs/2026-10-08-精度验收口径规范.md §7");
        var top1Channels = results
            .Where(item => item.CodingTop1Hit)
            .Select(item => item.Channels.Split('/')[0])
            .GroupBy(channel => channel, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count());
        Console.WriteLine("Top1 命中通道    " + string.Join("，", top1Channels.Select(group => $"{group.Key}={group.Count()}")));

        var misses = results.Where(item => !item.CodingTop1Hit).ToList();
        if (misses.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"未命中明细（{misses.Count} 条，供人工核对）：");
            foreach (var miss in misses.Take(30))
            {
                Console.WriteLine(
                    $"  {miss.CaseId} {miss.Name} 期望 {miss.ExpectedCode} → 实得 {miss.Top1Code ?? "（无推荐）"} [{miss.Channels}]");
            }

            if (misses.Count > 30)
            {
                Console.WriteLine($"  …其余 {misses.Count - 30} 条见报告文件");
            }
        }

        var report = new
        {
            version = "accuracy-report-v1",
            generatedAt = DateTimeOffset.UtcNow,
            pipeline = "phase1-v2.2-lite+vector+rerank",
            summary = new
            {
                totalCases,
                evaluated = total,
                caliber = "L3 精标语料口径（小规模人工构造，非万条口径）；分层判据与验收线归属见 docs/2026-10-08-精度验收口径规范.md",
                excludedMissingCode = warnings,
                codingTop1 = new { hit = codingTop1, total, percent = Percent(codingTop1, total), acceptance = "≥25%（决策 3 原线，归属待拍板）" },
                codingTop3 = new { hit = codingTop3, total, percent = Percent(codingTop3, total) },
                categoryTop1 = new { hit = categoryTop1, total, percent = Percent(categoryTop1, total), acceptance = "≥50%（决策 3 原线，归属待拍板）" },
                coverage = new { covered, total, percent = Percent(covered, total) },
                avgLatencyMs = total == 0 ? 0 : Math.Round(results.Average(item => item.LatencyMs), 0)
            },
            cases = results.Select(item => new
            {
                item.CaseId,
                item.Name,
                item.ExpectedCode,
                item.Top1Code,
                item.CodingTop1Hit,
                item.CodingTop3Hit,
                item.CategoryTop1Hit,
                item.Channels,
                latencyMs = Math.Round(item.LatencyMs)
            })
        };
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"报告已写入 {reportPath}");
        return 0;
    }

    private static async Task CleanupAsync(
        HospitalAiDbContext dbContext,
        Guid hospitalId,
        Guid patientId)
    {
        // ExecuteDelete 批量删除：不依赖被跟踪实体与 rowversion，克隆行同样能删。
        // audit_log 由 Runner 的审计写入，须先于医院删除（FK audit_log → hospital）。
        // 子表在前；CodeSystem 必须先于 Hospital（必需外键）。
        await dbContext.AuditLogs.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.MedicalCodeEmbeddings.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.RecommendationScores.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.CodingCandidates.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.QualityIssues.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.RecommendationEvidences.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.CodingDiagnosisInputs.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.CodingReviews.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.FinalCodingResults.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.PipelineTraceSteps.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.PipelineTraces.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.CodingRecommendations.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.ClinicalFactEvidences.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.ClinicalEvidences.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.ClinicalFacts.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.ClinicalEntities.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.DocumentSections.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.MedicalDocuments.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.CodingTasks.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.Visits.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.Patients.Where(item => item.Id == patientId).ExecuteDeleteAsync();
        await dbContext.MedicalCodes.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.TermSynonyms.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.CodingRules.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.CodeSystems.Where(item => item.HospitalId == hospitalId).ExecuteDeleteAsync();
        await dbContext.Hospitals.Where(item => item.Id == hospitalId).ExecuteDeleteAsync();
    }
}
