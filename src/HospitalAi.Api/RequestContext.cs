using HospitalAi.Application.Abstractions;

namespace HospitalAi.Api;

/// <summary>
/// 当前 HTTP 请求的上下文实现，由中间件填充并以 Scoped 生命周期注册。
/// </summary>
public sealed class RequestContext : IRequestContext
{
    public Guid HospitalId { get; internal set; }

    public string RequestId { get; internal set; } = string.Empty;

    public string TraceId { get; internal set; } = string.Empty;

    public string? IdempotencyKey { get; internal set; }

    public string? UserId { get; internal set; }
}
