using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.Traces;

/// <summary>
/// Pipeline Trace 查询响应。
/// </summary>
public sealed record TraceResponse(
    [property: JsonPropertyName("traceId")] string TraceId,
    [property: JsonPropertyName("hospitalId")] Guid HospitalId,
    [property: JsonPropertyName("taskId")] Guid? TaskId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("steps")] IReadOnlyList<TraceStepResponse> Steps);

/// <summary>
/// Pipeline Trace 步骤响应。
/// </summary>
public sealed record TraceStepResponse(
    [property: JsonPropertyName("stepName")] string StepName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("errorCode")] string? ErrorCode);
