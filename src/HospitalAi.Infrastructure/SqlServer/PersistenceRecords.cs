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
/// 编码任务持久化记录。
/// </summary>
public sealed class CodingTaskRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid VisitId { get; set; }

    public string PipelineVersion { get; set; } = string.Empty;

    public string? IdempotencyKey { get; set; }

    public CodingTaskStatus Status { get; set; }

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
}

/// <summary>
/// 医疗文书元数据记录。正文只保存外部引用和哈希。
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

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public HospitalRecord Hospital { get; set; } = null!;

    public VisitRecord Visit { get; set; } = null!;
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
/// Pipeline 步骤级追踪记录。
/// </summary>
public sealed class PipelineTraceStepRecord
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public Guid PipelineTraceId { get; set; }

    public string StepName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? ErrorCode { get; set; }

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
