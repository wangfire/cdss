namespace HospitalAi.Contracts.CodingTasks;

/// <summary>
/// 编码任务创建事件契约，供 API Outbox 和 Worker 共享。
/// </summary>
/// <remarks>
/// 消息类型串（<see cref="MessageType"/>）与队列实体名是消息契约的一部分：
/// 发布端（Outbox）、消费端（MassTransit ReceiveEndpoint）与僵死补偿必须一致，
/// 此前以字面量散落 8 处，改一处漏七处会让消息路由静默断裂。
/// </remarks>
public sealed record CodingTaskCreatedMessage(
    Guid MessageId,
    Guid HospitalId,
    Guid TaskId,
    Guid VisitId,
    string PipelineVersion,
    string TraceId,
    string? RequestId = null)
{
    /// <summary>消息类型标识（MassTransit message type 与 Outbox message_type 共用）。</summary>
    public const string MessageType = "coding.task.created";
}
