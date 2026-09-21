using HospitalAi.Domain.CodingTasks;

namespace HospitalAi.Application.Coding.Evidence;

/// <summary>
/// 构建出的临床证据。evidence_score = LevelWeight × SourceReliability ×
/// TemporalValidity × TextCompleteness；level 初始权重 A=1.00 / B=0.85 /
/// C=0.65 / D=0.40 / E=0.20。
/// </summary>
public sealed record ExtractedEvidence(
    string EvidenceType,
    string SourceType,
    Guid? DocumentId,
    Guid? SectionId,
    string OriginalText,
    int StartPosition,
    int EndPosition,
    EvidenceLevel Level,
    decimal SourceReliability,
    decimal TemporalValidity,
    decimal TextCompleteness,
    decimal EvidenceScore);

/// <summary>
/// V2.2-Lite 证据构建器。证据来自本次运行的文档切片：
/// 正式诊断 / 病理 / 明确检查结论 → A；病程、影像、检验、手术直接证据 → B；
/// 症状、体征等间接证据 → C；考虑、可能、推测性 → D；弱上下文 → E。
/// 没有任何证据时推荐结果必须是 NO_SAFE_RECOMMENDATION。
/// </summary>
public static class EvidenceBuilder
{
    public const decimal TextCompletenessWindow = 60;

    private static readonly Dictionary<EvidenceLevel, decimal> LevelWeights = new()
    {
        [EvidenceLevel.A] = 1.00m,
        [EvidenceLevel.B] = 0.85m,
        [EvidenceLevel.C] = 0.65m,
        [EvidenceLevel.D] = 0.40m,
        [EvidenceLevel.E] = 0.20m
    };

    public static decimal GetLevelWeight(EvidenceLevel level)
    {
        return LevelWeights.TryGetValue(level, out var weight) ? weight : 0m;
    }

    public static EvidenceLevel ResolveLevel(
        string documentType,
        string text,
        HospitalAi.Domain.CodingTasks.FactCertainty certainty)
    {
        if (certainty is HospitalAi.Domain.CodingTasks.FactCertainty.Suspected)
        {
            return EvidenceLevel.D;
        }

        var type = documentType.ToLowerInvariant();
        if (type.Contains("diagnosis") || type.Contains("病理") || type.Contains("诊断"))
        {
            return text.Contains("病原学", StringComparison.Ordinal)
                || text.Contains("明确", StringComparison.Ordinal)
                ? EvidenceLevel.A
                : EvidenceLevel.B;
        }

        if (type.Contains("病程") || type.Contains("手术") || type.Contains("影像")
            || type.Contains("检验") || type.Contains("检查报告"))
        {
            return EvidenceLevel.B;
        }

        if (type.Contains("主诉") || type.Contains("症状") || type.Contains("体征"))
        {
            return EvidenceLevel.C;
        }

        return EvidenceLevel.E;
    }

    public static ExtractedEvidence Build(
        HospitalAi.Application.Coding.Preprocessing.ExtractedFact fact,
        HospitalAi.Application.Coding.Preprocessing.TextChunk chunk,
        string documentType,
        Guid? documentId,
        FactTemporality temporality)
    {
        var level = ResolveLevel(documentType, fact.OriginalValue, fact.Certainty);
        var sourceReliability = level switch
        {
            EvidenceLevel.A => 1.0m,
            EvidenceLevel.B => 0.9m,
            EvidenceLevel.C => 0.8m,
            EvidenceLevel.D => 0.7m,
            _ => 0.5m
        };
        var temporalValidity = temporality switch
        {
            FactTemporality.Current => 1.0m,
            FactTemporality.Past => 0.8m,
            FactTemporality.Family => 0.6m,
            _ => 0.7m
        };
        var spanLength = Math.Max(1, fact.SourceEnd - fact.SourceStart);
        var textCompleteness = Math.Round(Math.Min(1.0m, spanLength / TextCompletenessWindow), 4);

        var score = Math.Round(
            GetLevelWeight(level) * sourceReliability * temporalValidity * textCompleteness,
            4);

        return new ExtractedEvidence(
            EvidenceType: level == EvidenceLevel.A ? "DIAGNOSTIC" : "CLINICAL",
            SourceType: documentType,
            DocumentId: documentId,
            SectionId: fact.SourceSectionId,
            OriginalText: fact.OriginalValue,
            StartPosition: fact.SourceStart,
            EndPosition: fact.SourceEnd,
            Level: level,
            SourceReliability: sourceReliability,
            TemporalValidity: temporalValidity,
            TextCompleteness: textCompleteness,
            EvidenceScore: score);
    }
}
