using System.Text.Json;

namespace HospitalAi.Tools;

/// <summary>
/// Golden Dataset 用例定义与静态校验。
/// 校验只检查数据本身：必填字段、场景覆盖、期望编码与安全断言的一致性。
/// </summary>
public sealed record GoldenCase
{
    public required string CaseId { get; init; }

    public required string Category { get; init; }

    public required string DocumentText { get; init; }

    public required string DiagnosisText { get; init; }

    public IReadOnlyList<string> ExpectedFacts { get; init; } = [];

    public IReadOnlyList<string> ExpectedNegation { get; init; } = [];

    public IReadOnlyList<string> ExpectedTemporality { get; init; } = [];

    public IReadOnlyList<string> ExpectedNormalization { get; init; } = [];

    public IReadOnlyList<string> ExpectedEvidence { get; init; } = [];

    public IReadOnlyList<string> ExpectedCodes { get; init; } = [];

    public string? ExpectedRisk { get; init; }

    public IReadOnlyList<string> SafetyAssertions { get; init; } = [];
}

/// <summary>
/// 方案要求的 12 类场景。缺一类就认为数据集覆盖不足，不允许作为上线依据。
/// </summary>
public static class GoldenCategories
{
    public static readonly string[] Required =
    [
        "standard",
        "synonym",
        "combination",
        "negation",
        "history",
        "family",
        "uncertain",
        "multi_document",
        "granularity",
        "conflict",
        "no_evidence",
        "procedure_mix"
    ];
}

/// <summary>
/// 安全断言清单。无模型环境下也必须成立，否则判为回归。
/// </summary>
public static class GoldenSafetyAssertions
{
    public const string HasEvidence = "HAS_EVIDENCE";
    public const string NoEvidenceNoRecommendation = "NO_EVIDENCE_NO_RECOMMENDATION";
    public const string NotHighConfidence = "NOT_HIGH_CONFIDENCE";

    public static readonly string[] Known =
    [
        HasEvidence,
        NoEvidenceNoRecommendation,
        NotHighConfidence,
        "NOT_LEGACY",
        "NEGATED_NOT_CODED",
        "FAMILY_NOT_CODED_AS_PATIENT",
        "TEMPORALITY_HISTORY",
        "UNCERTAIN_NOT_HIGH_CONFIDENCE",
        "MULTI_DOCUMENT_EVIDENCE",
        "GRANULARITY_ISSUE_RAISED",
        "CONFLICT_SURFACED",
        "DIAGNOSIS_PROCEDURE_SEPARATED"
    ];
}

public static class GoldenDataset
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task<IReadOnlyList<GoldenCase>> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<GoldenDatasetFile>(
            stream,
            SerializerOptions,
            cancellationToken);
        var cases = document?.Cases ?? [];
        if (cases.Count == 0)
        {
            throw new InvalidOperationException($"Golden 数据集为空：{path}");
        }

        return cases;
    }

    private sealed record GoldenDatasetFile(
        string? Version,
        IReadOnlyList<GoldenCase> Cases);
}
