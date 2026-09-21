using HospitalAi.Domain.CodingTasks;

namespace HospitalAi.Infrastructure.SqlServer;

/// <summary>
/// V2.2-Lite 临床事实。negation / certainty / temporality 与原文值、来源位置一起落库，
/// 未接入医疗小模型前只执行确定性规则，无法判断的文本不得伪造确定性。
/// </summary>
public sealed class ClinicalFactRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid VisitId { get; set; }

    public Guid CodingTaskId { get; set; }

    public Guid PipelineRunId { get; set; }

    public ClinicalFactType FactType { get; set; }

    public string FactName { get; set; } = string.Empty;

    public string? NormalizedValue { get; set; }

    public string OriginalValue { get; set; } = string.Empty;

    public bool Negation { get; set; }

    public FactCertainty Certainty { get; set; }

    public FactTemporality Temporality { get; set; }

    /// <summary>ACTIVE / LEGACY_READ_ONLY。Lite 阶段新写入均为 ACTIVE。</summary>
    public string Status { get; set; } = "ACTIVE";

    public decimal Confidence { get; set; }

    public Guid? SourceDocumentId { get; set; }

    public Guid? SourceSectionId { get; set; }

    public int? SourceStart { get; set; }

    public int? SourceEnd { get; set; }

    public string ExtractorVersion { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// V2.2-Lite 临床证据。evidence_score = LevelWeight × SourceReliability × TemporalValidity × TextCompleteness。
/// </summary>
public sealed class ClinicalEvidenceRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid VisitId { get; set; }

    public Guid CodingTaskId { get; set; }

    public Guid PipelineRunId { get; set; }

    public string EvidenceType { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public Guid? DocumentId { get; set; }

    public Guid? SectionId { get; set; }

    public string OriginalText { get; set; } = string.Empty;

    public int StartPosition { get; set; }

    public int EndPosition { get; set; }

    public EvidenceLevel EvidenceLevel { get; set; }

    public decimal SourceReliability { get; set; }

    public decimal TemporalValidity { get; set; }

    public decimal TextCompleteness { get; set; }

    public decimal EvidenceScore { get; set; }

    /// <summary>ACTIVE / LEGACY_READ_ONLY。</summary>
    public string Status { get; set; } = "ACTIVE";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Fact 与 Evidence 的关系表。一条事实可由多条证据支撑。
/// </summary>
public sealed class ClinicalFactEvidenceRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid FactId { get; set; }

    public Guid EvidenceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// V2.2-Lite 医生 / 编码员原始诊断输入。AI 标准化结果写入 normalized_text 时
/// 必须保留 original_text 原文，禁止覆盖。
/// </summary>
public sealed class CodingDiagnosisInputRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid VisitId { get; set; }

    public Guid CodingTaskId { get; set; }

    /// <summary>DOCUMENT_AUTO / STRUCTURED_INPUT。</summary>
    public string SourceType { get; set; } = string.Empty;

    public string OriginalText { get; set; } = string.Empty;

    public string? NormalizedText { get; set; }

    public bool IsPrincipal { get; set; }

    public int DiagnosisOrder { get; set; }

    public Guid? SourceDocumentId { get; set; }

    public Guid? SourceSectionId { get; set; }

    public int? SourceStart { get; set; }

    public int? SourceEnd { get; set; }

    /// <summary>ACTIVE / LEGACY_READ_ONLY。</summary>
    public string Status { get; set; } = "ACTIVE";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// V2.2-Lite 编码候选项。保留 Recall 与排序过程分，缺失维度为 null 而不是填默认值。
/// </summary>
public sealed class CodingCandidateRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid CodingTaskId { get; set; }

    public Guid DiagnosisInputId { get; set; }

    public Guid PipelineRunId { get; set; }

    public string CodeSystem { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>EXACT / BM25 / VECTOR / RERANK / COMBINED。</summary>
    public string RecallSource { get; set; } = string.Empty;

    public decimal? ExactScore { get; set; }

    public decimal? Bm25Score { get; set; }

    public decimal? VectorScore { get; set; }

    public decimal? RerankScore { get; set; }

    public decimal? RuleScore { get; set; }

    public decimal? EvidenceScore { get; set; }

    public decimal FinalScore { get; set; }

    public int Rank { get; set; }

    /// <summary>ACTIVE / STALE / SUPERSEDED。</summary>
    public string Status { get; set; } = "ACTIVE";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// V2.2-Lite 七维分数。缺失维度按实际参与维度重新归一化，缺失值保持 null。
/// score_profile 记录评分口径（FULL / FAST / DEGRADED.*），跨口径分数不横向比较。
/// </summary>
public sealed class RecommendationScoreRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid RecommendationId { get; set; }

    public decimal? ExactScore { get; set; }

    public decimal? SemanticScore { get; set; }

    public decimal? RetrievalScore { get; set; }

    public decimal? RerankScore { get; set; }

    public decimal? RuleScore { get; set; }

    public decimal? EvidenceScore { get; set; }

    public decimal? LlmScore { get; set; }

    public decimal? MarginScore { get; set; }

    public string ScoreProfile { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// V2.2-Lite 结构化质量问题。Lite 阶段仅 EVIDENCE_INSUFFICIENT / RULE_VIOLATION /
/// GRANULARITY_INSUFFICIENT 三种类型。
/// </summary>
public sealed class QualityIssueRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid VisitId { get; set; }

    public Guid CodingTaskId { get; set; }

    public Guid? DiagnosisInputId { get; set; }

    public QualityIssueType IssueType { get; set; }

    /// <summary>LOW / MEDIUM / HIGH / CRITICAL。</summary>
    public string RiskLevel { get; set; } = "LOW";

    public string Description { get; set; } = string.Empty;

    public Guid? FactId { get; set; }

    /// <summary>JSON 数组字符串， EvidenceId 列表。前端点击问题须能定位原文。</summary>
    public string? EvidenceIds { get; set; }

    public string? CurrentCode { get; set; }

    public string? SuggestedCode { get; set; }

    /// <summary>OPEN / RESOLVED / IGNORED。</summary>
    public string Status { get; set; } = "OPEN";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
