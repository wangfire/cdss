using HospitalAi.Domain.CodingTasks;

namespace HospitalAi.Infrastructure.SqlServer;

/// <summary>
/// 医院持久化记录。
/// </summary>
public sealed class HospitalRecord
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ICollection<PatientRecord> Patients { get; } = [];

    public ICollection<VisitRecord> Visits { get; } = [];

    public ICollection<CodingTaskRecord> CodingTasks { get; } = [];

    public ICollection<CodeSystemRecord> CodeSystems { get; } = [];
}

/// <summary>
/// 患者持久化记录。患者显示名只在业务明确需要时保存，日志不得记录该字段。
/// </summary>
public sealed class PatientRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public string SourceSystem { get; set; } = string.Empty;

    public string SourcePatientId { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public ICollection<VisitRecord> Visits { get; } = [];
}

/// <summary>
/// 就诊持久化记录。
/// </summary>
public sealed class VisitRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid PatientId { get; set; }

    public DateTimeOffset AdmissionAt { get; set; }

    public DateTimeOffset? DischargeAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public PatientRecord Patient { get; set; } = null!;

    public ICollection<CodingTaskRecord> CodingTasks { get; } = [];
}

/// <summary>
/// 编码任务持久化记录。Status 是技术任务状态，CodingStage 是业务阶段状态，两者分离。
/// </summary>
public sealed class CodingTaskRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid VisitId { get; set; }

    public string PipelineVersion { get; set; } = string.Empty;

    public string? IdempotencyKey { get; set; }

    public CodingTaskStatus Status { get; set; }

    /// <summary>V2.2 业务阶段：IMPORTED / PROCESSING / READY / AI_RECOMMENDING / CODER_REVIEW / FINAL_PENDING / FINALIZED / HUMAN_REQUIRED。</summary>
    public CodingStage CodingStage { get; set; } = CodingStage.Imported;

    public int RetryCount { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? ErrorCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public VisitRecord Visit { get; set; } = null!;

    public ICollection<PipelineTraceRecord> PipelineTraces { get; } = [];

    public ICollection<CodingRecommendationRecord> CodingRecommendations { get; } = [];
}

/// <summary>
/// 编码体系记录，如 ICD-10、ICD-9-CM-3。
/// </summary>
public sealed class CodeSystemRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public ICollection<MedicalCodeRecord> MedicalCodes { get; } = [];
}

/// <summary>
/// 医学编码字典记录。
/// </summary>
public sealed class MedicalCodeRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid CodeSystemId { get; set; }

    public string CodeSystemCode { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string CodeType { get; set; } = string.Empty;

    public string SearchText { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public CodeSystemRecord CodeSystem { get; set; } = null!;
}

/// <summary>
/// 医学术语同义词记录。
/// </summary>
public sealed class TermSynonymRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public string Term { get; set; } = string.Empty;

    public string NormalizedTerm { get; set; } = string.Empty;

    public string CodeSystemCode { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;
}

/// <summary>
/// 应用用户持久化记录，RBAC 最小模型的用户身份。
/// 含密码哈希（PBKDF2），支持用户名/密码登录。
/// </summary>
public sealed class AppUserRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// PBKDF2 密码哈希，格式：v1$&lt;saltBase64&gt;$&lt;hashBase64&gt;。
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// 最后登录时间（UTC）。
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;
}

/// <summary>
/// 应用角色持久化记录，RBAC 最小模型的角色定义。
/// </summary>
public sealed class AppRoleRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;
}

/// <summary>
/// 用户-角色关联持久化记录，保证同一用户同一角色只分配一次。
/// </summary>
public sealed class AppUserRoleRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid AppUserId { get; set; }

    public Guid AppRoleId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public AppUserRecord AppUser { get; set; } = null!;

    public AppRoleRecord AppRole { get; set; } = null!;
}

/// <summary>
/// 编码规则记录。V2.2-Lite 起支持版本化 JSON 条件规则（ConditionJson / ActionJson），
/// 旧平面规则沿用 code_pattern + rule_type 字段，ConditionJson 为空时按兼容模式评估。
/// </summary>
public sealed class CodingRuleRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public string RuleCode { get; set; } = string.Empty;

    public string RuleVersion { get; set; } = "v1";

    public string CodeSystemCode { get; set; } = string.Empty;

    public string CodePattern { get; set; } = string.Empty;

    public string RuleType { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>数值越小优先级越高。内置规则与医院自定义规则按 is_builtin 区分先后。</summary>
    public int Priority { get; set; } = 100;

    /// <summary>互斥组：同一 group 内只取优先级最高（数值最小）的命中规则。</summary>
    public string? RuleGroup { get; set; }

    public string? ConditionJson { get; set; }

    public string? ActionJson { get; set; }

    public bool Blocking { get; set; }

    public bool IsBuiltin { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;
}

/// <summary>
/// 医疗文书元数据记录。正文只保存外部引用和哈希。
/// V2.2-Lite 复用本表作为文档版本入口，parse_version / ocr_version / is_current
/// 描述版本状态；Full 阶段再拆出独立 medical_document_version。
/// </summary>
public sealed class MedicalDocumentRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid VisitId { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    public string ContentReference { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public int Version { get; set; }

    public string? ParseVersion { get; set; }

    public string? OcrVersion { get; set; }

    /// <summary>ACTIVE / STALE / PENDING_PARSE / HUMAN_REQUIRED。</summary>
    public string DocumentStatus { get; set; } = "ACTIVE";

    /// <summary>增量更新来源文档（同一 visit 下的前一版本）。</summary>
    public Guid? SourceDocumentId { get; set; }

    public DateTimeOffset? SourceUpdatedAt { get; set; }

    /// <summary>同一就诊同类型文书是否当前生效版本。</summary>
    public bool IsCurrent { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public VisitRecord Visit { get; set; } = null!;
}

/// <summary>
/// 文书段落记录。V2.2-Lite 同时作为 Chunk 使用，记录原文位置、token 近似数与索引状态。
/// </summary>
public sealed class DocumentSectionRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid VisitId { get; set; }

    public Guid MedicalDocumentId { get; set; }

    public string SectionType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int Sequence { get; set; }

    /// <summary>Chunk 在原文中的起始偏移。</summary>
    public int StartPosition { get; set; }

    /// <summary>Chunk 在原文中的结束偏移。</summary>
    public int EndPosition { get; set; }

    /// <summary>token 近似长度，用于上下文预算控制。</summary>
    public int TokenCount { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    /// <summary>PENDING / DONE / UNAVAILABLE。</summary>
    public string EmbeddingStatus { get; set; } = "PENDING";

    /// <summary>PENDING / INDEXED / UNAVAILABLE。</summary>
    public string IndexStatus { get; set; } = "PENDING";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public VisitRecord Visit { get; set; } = null!;

    public MedicalDocumentRecord MedicalDocument { get; set; } = null!;
}

/// <summary>
/// 基线实体识别结果记录。
/// </summary>
public sealed class ClinicalEntityRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid CodingTaskId { get; set; }

    public Guid? DocumentSectionId { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string RawText { get; set; } = string.Empty;

    public string NormalizedText { get; set; } = string.Empty;

    public bool IsNegated { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public CodingTaskRecord CodingTask { get; set; } = null!;

    public DocumentSectionRecord? DocumentSection { get; set; }
}

/// <summary>
/// AI 编码推荐记录。旧 MVP 与新 V2.2 共用此表，通过 PipelineVersion 与
/// LifecycleStatus 区分：旧数据标记 phase2-mvp-legacy + LEGACY_READ_ONLY，只读。
/// </summary>
public sealed class CodingRecommendationRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid CodingTaskId { get; set; }

    /// <summary>V2.2 诊断输入标识；旧 MVP 推荐为 null。</summary>
    public Guid? DiagnosisInputId { get; set; }

    public string PipelineVersion { get; set; } = string.Empty;

    /// <summary>本次推荐运行标识，用于 Trace、Candidate、Score 关联。</summary>
    public Guid? PipelineRunId { get; set; }

    /// <summary>同一诊断输入的推荐版本号，重跑递增。</summary>
    public string? RecommendationVersion { get; set; }

    public string RecommendationType { get; set; } = string.Empty;

    public string CodeSystemCode { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Rank { get; set; }

    public decimal RecallScore { get; set; }

    public decimal RuleScore { get; set; }

    public decimal ConfidenceScore { get; set; }

    public string ReviewStatus { get; set; } = string.Empty;

    /// <summary>HIGH_CONFIDENCE / NEED_REVIEW / NO_SAFE_RECOMMENDATION / HUMAN_REQUIRED / LEGACY / UNKNOWN。</summary>
    public string Outcome { get; set; } = "UNKNOWN";

    /// <summary>生命周期：ACTIVE / STALE / LEGACY_READ_ONLY / SUPERSEDED。</summary>
    public string LifecycleStatus { get; set; } = "ACTIVE";

    public decimal? EvidenceSufficiency { get; set; }

    public string? RiskLevel { get; set; }

    public string? Reason { get; set; }

    public string? ModelVersion { get; set; }

    public string? PromptVersion { get; set; }

    public string? KnowledgeVersion { get; set; }

    public string? RuleVersion { get; set; }

    public string? CodingVersion { get; set; }

    public bool IsReadOnly { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public CodingTaskRecord CodingTask { get; set; } = null!;

    public ICollection<RecommendationEvidenceRecord> Evidences { get; } = [];
}

/// <summary>
/// 编码推荐证据记录。
/// </summary>
public sealed class RecommendationEvidenceRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid CodingRecommendationId { get; set; }

    public Guid? DocumentSectionId { get; set; }

    /// <summary>V2.2 运行标识；旧 MVP 证据为 null。</summary>
    public Guid? PipelineRunId { get; set; }

    /// <summary>V2.2 证据等级 A-E；旧 MVP 证据为 null。</summary>
    public string? EvidenceLevel { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public string SourceText { get; set; } = string.Empty;

    public string MatchText { get; set; } = string.Empty;

    public decimal Score { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public CodingRecommendationRecord CodingRecommendation { get; set; } = null!;

    public DocumentSectionRecord? DocumentSection { get; set; }
}

/// <summary>
/// 人工审核记录。
/// </summary>
public sealed class CodingReviewRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid CodingTaskId { get; set; }

    public string ReviewStatus { get; set; } = string.Empty;

    public string? Comment { get; set; }

    public string? ReviewerId { get; set; }

    /// <summary>V2.2 审核动作类型：ACCEPTED / REJECTED / MODIFIED。</summary>
    public string? ResultType { get; set; }

    public DateTimeOffset ReviewedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public CodingTaskRecord CodingTask { get; set; } = null!;
}

/// <summary>
/// 人工确认后的最终编码结果。
/// </summary>
public sealed class FinalCodingResultRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid CodingTaskId { get; set; }

    public Guid? SourceRecommendationId { get; set; }

    public string ResultType { get; set; } = string.Empty;

    public string CodeSystemCode { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? ReviewerId { get; set; }

    public DateTimeOffset ConfirmedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public CodingTaskRecord CodingTask { get; set; } = null!;

    public CodingRecommendationRecord? SourceRecommendation { get; set; }
}

/// <summary>
/// Outbox 消息持久化记录。本阶段只定义模型，不实现发布行为。
/// </summary>
public sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public string MessageType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;
}

/// <summary>
/// Inbox 消息持久化记录。本阶段只定义模型，不实现幂等行为。
/// </summary>
public sealed class InboxMessageRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid MessageId { get; set; }

    public string ConsumerName { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;
}

/// <summary>
/// Pipeline 任务级追踪记录。
/// </summary>
public sealed class PipelineTraceRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid? CodingTaskId { get; set; }

    public string TraceId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public CodingTaskRecord? CodingTask { get; set; }

    public ICollection<PipelineTraceStepRecord> Steps { get; } = [];
}

/// <summary>
/// Pipeline 步骤级追踪记录。V2.2 增加 pipeline_run_id / diagnosis_input_id / stage
/// 与模型、知识、规则版本和 token 记录；一个任务的多个运行、多个诊断输入可共存。
/// </summary>
public sealed class PipelineTraceStepRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid PipelineTraceId { get; set; }

    /// <summary>V2.2 运行标识；旧 MVP 步骤为 null。</summary>
    public Guid? PipelineRunId { get; set; }

    /// <summary>V2.2 诊断输入标识；任务级步骤为 null。</summary>
    public Guid? DiagnosisInputId { get; set; }

    /// <summary>QUALITY_GATE / DOCUMENT_VERSION / CHUNK / FACT / EVIDENCE / EXACT_RETRIEVAL /
    /// BM25_RETRIEVAL / RULE / SCORE / POLICY / PERSIST。</summary>
    public string Stage { get; set; } = string.Empty;

    public string StepName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? ErrorCode { get; set; }

    public long? DurationMs { get; set; }

    public string? ModelVersion { get; set; }

    public string? PromptVersion { get; set; }

    public string? KnowledgeVersion { get; set; }

    public string? RuleVersion { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public PipelineTraceRecord PipelineTrace { get; set; } = null!;
}

/// <summary>
/// 审计日志持久化记录。
/// </summary>
public sealed class AuditLogRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public string ResourceType { get; set; } = string.Empty;

    public Guid ResourceId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Result { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    public string? ActorId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;
}
