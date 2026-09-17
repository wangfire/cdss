namespace HospitalAi.Contracts.CodingTasks;

/// <summary>
/// 编码任务创建事件契约，供 API Outbox 和 Worker 共享。
/// </summary>
public sealed record CodingTaskCreatedMessage(
    Guid MessageId,
    Guid HospitalId,
    Guid TaskId,
    Guid VisitId,
    string PipelineVersion,
    string TraceId,
    string? RequestId = null);
