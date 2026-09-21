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

/// <summary>
/// 单次流水线运行（按 pipeline_run_id 聚合）内的一个步骤。
/// </summary>
public sealed record PipelineRunStepResponse(
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("durationMs")] long? DurationMs);

/// <summary>
/// 运行期间产生的结构化质量问题。
/// </summary>
public sealed record PipelineRunIssueResponse(
    [property: JsonPropertyName("issueType")] string IssueType,
    [property: JsonPropertyName("riskLevel")] string RiskLevel,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("currentCode")] string? CurrentCode,
    [property: JsonPropertyName("diagnosisInputId")] Guid? DiagnosisInputId);

/// <summary>
/// 病例（编码任务）的一次流水线运行记录。
/// </summary>
public sealed record PipelineRunResponse(
    [property: JsonPropertyName("runId")] Guid? RunId,
    [property: JsonPropertyName("traceId")] string TraceId,
    [property: JsonPropertyName("pipelineVersion")] string? PipelineVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("durationMs")] long? DurationMs,
    [property: JsonPropertyName("degradedFlags")] IReadOnlyList<string> DegradedFlags,
    [property: JsonPropertyName("recommendationCount")] int RecommendationCount,
    [property: JsonPropertyName("steps")] IReadOnlyList<PipelineRunStepResponse> Steps,
    [property: JsonPropertyName("qualityIssues")] IReadOnlyList<PipelineRunIssueResponse> QualityIssues);
