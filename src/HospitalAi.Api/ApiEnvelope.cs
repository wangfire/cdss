using System.Text.Json.Serialization;

namespace HospitalAi.Api;

/// <summary>
/// 统一成功响应体。所有成功端点用 <see cref="ApiEnvelope"/> 包装后
/// <c>Results.Ok(ApiEnvelope.Create(data))</c> 返回，序列化为
/// <c>{ code: 0, message: "ok", data: ... }</c>，与前端 api/http.ts 的
/// ApiEnvelope 解析逻辑（code=0 视为成功）完全对齐。
/// </summary>
public static class ApiEnvelope
{
    /// <summary>
    /// 创建 code=0 的成功响应体。
    /// </summary>
    public static Envelope<T> Create<T>(T data, string message = "ok")
        => new(0, message, data);
}

/// <summary>
/// 统一 API 响应包装（成功）。
/// </summary>
public sealed record Envelope<T>(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")] T Data);
