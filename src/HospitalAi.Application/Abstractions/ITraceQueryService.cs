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

    /// <summary>
    /// 按编码任务列出历次流水线运行（含步骤时间线、降级标记、质量问题），最近的运行优先。
    /// </summary>
    Task<IReadOnlyList<PipelineRunResponse>> ListRunsAsync(
        Guid taskId,
        int limit = 20,
        CancellationToken cancellationToken = default);
}
