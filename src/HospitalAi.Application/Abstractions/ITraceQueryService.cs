using HospitalAi.Contracts.Traces;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// 查询当前医院范围内的 Pipeline Trace。
/// </summary>
public interface ITraceQueryService
{
    Task<TraceResponse?> GetAsync(
        string traceId,
        CancellationToken cancellationToken = default);
}
