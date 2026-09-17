using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.Common;

/// <summary>
/// 统一 API 错误响应，避免不同接口返回不一致的错误结构。
/// </summary>
public sealed record ErrorResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("traceId")] string TraceId,
    [property: JsonPropertyName("details")] object? Details);
