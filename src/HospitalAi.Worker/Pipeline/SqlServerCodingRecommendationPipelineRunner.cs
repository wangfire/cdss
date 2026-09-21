using System.Text.RegularExpressions;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// Phase 2 智能编码推荐 MVP 流水线，使用规则和 SQL 字典完成可解释推荐。
/// 注意：coding_rule 表为预留，规则引擎消费在后续 Phase。当前流水线仅使用
/// 编码字典（MedicalCodes）与同义词（TermSynonyms）做召回，规则库未参与推荐打分。
///
/// 只声明处理 Legacy 版本：V2.2 新任务不得进入本 Runner。
/// </summary>
public sealed partial class SqlServerCodingRecommendationPipelineRunner(
    HospitalAiDbContext dbContext) : ICodingTaskPipelineRunner, ICodingTaskPipelineCapabilities
{
    private static readonly string[] SupportedVersions = [PipelineVersions.Legacy];

    private const decimal ReviewThreshold = 0.85m;

    /// <summary>仅用于历史任务回放，明文拒绝新 V2.2 任务。</summary>
    public IReadOnlyCollection<string> SupportedPipelineVersions => SupportedVersions;

    public async Task RunAsync(
        CodingTaskCreatedMessage message,
        CancellationToken cancellationToken = default)
    {
        var task = await dbContext.CodingTasks.SingleAsync(
            item => item.Id == message.TaskId
                && item.HospitalId == message.HospitalId,
            cancellationToken);
        var documents = await dbContext.MedicalDocuments
            .Where(item => item.HospitalId == message.HospitalId && item.VisitId == message.VisitId)
            .OrderBy(item => item.DocumentType)
            .ThenBy(item => item.Version)
            .ToListAsync(cancellationToken);

        await ClearPreviousRunAsync(message, cancellationToken);
        var sections = await CreateSectionsAsync(message, documents, cancellationToken);
        var codes = await dbContext.MedicalCodes
            .AsNoTracking()
            .Where(item => item.HospitalId == message.HospitalId && item.IsEnabled)
            .OrderBy(item => item.CodeSystemCode)
            .ThenBy(item => item.Code)
            .ToListAsync(cancellationToken);
        var synonyms = await dbContext.TermSynonyms
            .AsNoTracking()
            .Where(item => item.HospitalId == message.HospitalId)
            .OrderByDescending(item => item.Term.Length)
            .ThenBy(item => item.Term)
            .ToListAsync(cancellationToken);

        var recommendations = BuildRecommendations(message, sections, codes, synonyms);
        dbContext.CodingRecommendations.AddRange(recommendations);

        var hasReviewableRecommendation = recommendations.Count > 0
            && recommendations.All(item => item.ConfidenceScore >= ReviewThreshold);
        var now = DateTimeOffset.UtcNow;
        task.Status = hasReviewableRecommendation
            ? CodingTaskStatus.PendingReview
            : CodingTaskStatus.HumanRequired;
        task.CompletedAt = now;
        task.ErrorCode = hasReviewableRecommendation ? null : "LOW_CONFIDENCE";
        task.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearPreviousRunAsync(
        CodingTaskCreatedMessage message,
        CancellationToken cancellationToken)
    {
        var recommendations = await dbContext.CodingRecommendations
            .Include(item => item.Evidences)
            .Where(item => item.HospitalId == message.HospitalId && item.CodingTaskId == message.TaskId)
            .ToListAsync(cancellationToken);
        dbContext.RecommendationEvidences.RemoveRange(recommendations.SelectMany(item => item.Evidences));
        dbContext.CodingRecommendations.RemoveRange(recommendations);

        var entities = await dbContext.ClinicalEntities
            .Where(item => item.HospitalId == message.HospitalId && item.CodingTaskId == message.TaskId)
            .ToListAsync(cancellationToken);
        dbContext.ClinicalEntities.RemoveRange(entities);

        var sections = await dbContext.DocumentSections
            .Where(item => item.HospitalId == message.HospitalId && item.VisitId == message.VisitId)
            .ToListAsync(cancellationToken);
        dbContext.DocumentSections.RemoveRange(sections);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DocumentSectionRecord>> CreateSectionsAsync(
        CodingTaskCreatedMessage message,
        IReadOnlyList<MedicalDocumentRecord> documents,
        CancellationToken cancellationToken)
    {
        var sections = new List<DocumentSectionRecord>();
        var sequence = 1;
        var now = DateTimeOffset.UtcNow;
        foreach (var document in documents)
        {
            foreach (var paragraph in SplitContent(ReadDocumentContent(document.ContentReference)))
            {
                var section = new DocumentSectionRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = message.HospitalId,
                    VisitId = message.VisitId,
                    MedicalDocumentId = document.Id,
                    SectionType = InferSectionType(paragraph),
                    Title = InferSectionTitle(paragraph),
                    Content = paragraph,
                    Sequence = sequence++,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                sections.Add(section);
                dbContext.DocumentSections.Add(section);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return sections;
    }

    private List<CodingRecommendationRecord> BuildRecommendations(
        CodingTaskCreatedMessage message,
        IReadOnlyList<DocumentSectionRecord> sections,
        IReadOnlyList<MedicalCodeRecord> codes,
        IReadOnlyList<TermSynonymRecord> synonyms)
    {
        var matches = new List<RecommendationMatch>();
        var codeLookup = codes.ToDictionary(
            item => BuildCodeKey(item.CodeSystemCode, item.Code),
            StringComparer.OrdinalIgnoreCase);

        foreach (var section in sections)
        {
            var normalizedContent = Normalize(section.Content);
            var matchedCodeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var synonym in synonyms)
            {
                var normalizedTerm = Normalize(synonym.Term);
                if (string.IsNullOrWhiteSpace(normalizedTerm)
                    || string.IsNullOrWhiteSpace(synonym.Code)
                    || !normalizedContent.Contains(normalizedTerm, StringComparison.Ordinal)
                    || ContainsNegation(section.Content, synonym.Term))
                {
                    continue;
                }

                var codeKey = BuildCodeKey(synonym.CodeSystemCode, synonym.Code);
                var code = codeLookup.TryGetValue(codeKey, out var dictionaryCode)
                    ? dictionaryCode
                    : CreateTransientCode(synonym);
                matchedCodeKeys.Add(codeKey);
                matches.Add(new RecommendationMatch(code, section, 0.95m, synonym.Term));
                AddClinicalEntity(
                    message,
                    section,
                    code,
                    synonym.Term,
                    synonym.NormalizedTerm,
                    false);
            }

            foreach (var code in codes)
            {
                if (matchedCodeKeys.Contains(BuildCodeKey(code.CodeSystemCode, code.Code)))
                {
                    continue;
                }

                var score = Score(section.Content, code);
                if (score < ReviewThreshold)
                {
                    continue;
                }

                matches.Add(new RecommendationMatch(code, section, score, code.Title));
                AddClinicalEntity(
                    message,
                    section,
                    code,
                    code.Title,
                    code.Title,
                    ContainsNegation(section.Content, code.Title));
            }
        }

        var now = DateTimeOffset.UtcNow;
        return matches
            .GroupBy(item => new
            {
                Type = ToRecommendationType(item.Code),
                item.Code.CodeSystemCode,
                item.Code.Code
            })
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .GroupBy(item => ToRecommendationType(item.Code))
            .SelectMany(group => group
                .OrderByDescending(item => item.Score)
                .Select((match, index) => CreateRecommendation(message, match, index + 1, now)))
            .ToList();
    }

    private void AddClinicalEntity(
        CodingTaskCreatedMessage message,
        DocumentSectionRecord section,
        MedicalCodeRecord code,
        string rawText,
        string normalizedText,
        bool isNegated)
    {
        var now = DateTimeOffset.UtcNow;
        dbContext.ClinicalEntities.Add(new ClinicalEntityRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = message.HospitalId,
            CodingTaskId = message.TaskId,
            DocumentSectionId = section.Id,
            EntityType = ToRecommendationType(code),
            RawText = rawText,
            NormalizedText = normalizedText,
            IsNegated = isNegated,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static CodingRecommendationRecord CreateRecommendation(
        CodingTaskCreatedMessage message,
        RecommendationMatch match,
        int rank,
        DateTimeOffset now)
    {
        var recommendation = new CodingRecommendationRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = message.HospitalId,
            CodingTaskId = message.TaskId,
            RecommendationType = ToRecommendationType(match.Code),
            CodeSystemCode = match.Code.CodeSystemCode,
            Code = match.Code.Code,
            Title = match.Code.Title,
            Rank = rank,
            RecallScore = match.Score,
            RuleScore = 0.95m,
            ConfidenceScore = Math.Round((match.Score * 0.7m) + 0.285m, 4),
            ReviewStatus = "PENDING_REVIEW",
            CreatedAt = now,
            UpdatedAt = now
        };
        recommendation.Evidences.Add(new RecommendationEvidenceRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = message.HospitalId,
            CodingRecommendationId = recommendation.Id,
            DocumentSectionId = match.Section.Id,
            SourceType = match.Section.SectionType,
            SourceText = TrimEvidence(match.Section.Content),
            MatchText = match.MatchedTerm,
            Score = match.Score,
            CreatedAt = now,
            UpdatedAt = now
        });
        return recommendation;
    }

    private static MedicalCodeRecord CreateTransientCode(TermSynonymRecord synonym)
    {
        // 同义词可能先于完整编码字典导入；组合编码必须原样作为一个候选保存。
        return new MedicalCodeRecord
        {
            CodeSystemCode = synonym.CodeSystemCode,
            Code = synonym.Code,
            Title = string.IsNullOrWhiteSpace(synonym.NormalizedTerm)
                ? synonym.Term
                : synonym.NormalizedTerm,
            CodeType = synonym.EntityType,
            SearchText = synonym.Term,
            IsEnabled = true
        };
    }

    private static string BuildCodeKey(string codeSystemCode, string code) =>
        $"{codeSystemCode}\u001F{code}";

    private static decimal Score(string content, MedicalCodeRecord code)
    {
        var normalizedContent = Normalize(content);
        var normalizedTitle = Normalize(code.Title);
        if (normalizedContent.Contains(normalizedTitle, StringComparison.Ordinal))
        {
            return ContainsNegation(content, code.Title) ? 0.20m : 0.95m;
        }

        var terms = SplitTerms(code.SearchText);
        if (terms.Count == 0)
        {
            return 0m;
        }

        var hits = terms.Count(term => normalizedContent.Contains(Normalize(term), StringComparison.Ordinal));
        if (hits == terms.Count)
        {
            return 0.88m;
        }

        return hits >= Math.Max(2, terms.Count - 1) ? 0.72m : 0m;
    }

    private static string ReadDocumentContent(string contentReference)
    {
        return File.Exists(contentReference)
            ? File.ReadAllText(contentReference)
            : contentReference;
    }

    private static IReadOnlyList<string> SplitContent(string content)
    {
        return content
            .Split(['\r', '\n', '。', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static string InferSectionType(string content)
    {
        if (content.Contains("手术", StringComparison.Ordinal)
            || content.Contains("操作", StringComparison.Ordinal))
        {
            return "PROCEDURE";
        }

        return "DIAGNOSIS";
    }

    private static string InferSectionTitle(string content)
    {
        var index = content.IndexOf('：');
        return index > 0 ? content[..index] : InferSectionType(content);
    }

    private static string ToRecommendationType(MedicalCodeRecord code)
    {
        if (code.CodeSystemCode.Contains("9", StringComparison.OrdinalIgnoreCase)
            || code.CodeType.Contains("手术", StringComparison.OrdinalIgnoreCase)
            || code.CodeType.Contains("PROCEDURE", StringComparison.OrdinalIgnoreCase))
        {
            return "PROCEDURE";
        }

        return "DIAGNOSIS";
    }

    private static bool ContainsNegation(string content, string title)
    {
        var index = content.IndexOf(title, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var prefix = content[Math.Max(0, index - 8)..index];
        return prefix.Contains('无', StringComparison.Ordinal)
            || prefix.Contains("否认", StringComparison.Ordinal)
            || prefix.Contains("未见", StringComparison.Ordinal);
    }

    private static string TrimEvidence(string content)
    {
        return content.Length <= 400 ? content : content[..400];
    }

    private static string Normalize(string value)
    {
        return WhitespaceRegex().Replace(value, string.Empty).ToUpperInvariant();
    }

    private static IReadOnlyList<string> SplitTerms(string value)
    {
        return value
            .Split([' ', ',', '，', '/', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private sealed record RecommendationMatch(
        MedicalCodeRecord Code,
        DocumentSectionRecord Section,
        decimal Score,
        string MatchedTerm);
}
