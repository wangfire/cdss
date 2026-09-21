using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.CodingTasks;

/// <summary>
/// 创建编码任务请求。pipelineVersion 缺省时使用配置的默认版本（灰度开关）。
/// </summary>
public sealed record CreateCodingTaskRequest(
    [property: JsonPropertyName("visitId")] Guid VisitId,
    [property: JsonPropertyName("pipelineVersion")] string? PipelineVersion);

/// <summary>
/// 编码任务响应，不直接暴露领域实体。
/// </summary>
public sealed record CodingTaskResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("hospitalId")] Guid HospitalId,
    [property: JsonPropertyName("visitId")] Guid VisitId,
    [property: JsonPropertyName("pipelineVersion")] string PipelineVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("traceId")] string TraceId,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt);
