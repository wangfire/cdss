namespace HospitalAi.Application.Abstractions;

/// <summary>
/// 当前请求的最小上下文，供应用服务执行医院隔离和链路追踪。
/// </summary>
public interface IRequestContext
{
    Guid HospitalId { get; }

    string RequestId { get; }

    string TraceId { get; }

    string? IdempotencyKey { get; }

    string? UserId { get; }
}
