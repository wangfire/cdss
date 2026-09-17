using HospitalAi.Application.Abstractions;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Contracts.Traces;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.Tracing;

/// <summary>
/// 查询医院范围内的 Pipeline Trace 及其步骤。
/// </summary>
public sealed class SqlServerTraceQueryService(
    HospitalAiDbContext dbContext,
    IRequestContext requestContext) : ITraceQueryService
{
    public async Task<TraceResponse?> GetAsync(
        string traceId,
        CancellationToken cancellationToken = default)
    {
        if (requestContext.HospitalId == Guid.Empty)
        {
            return null;
        }

        var trace = await dbContext.PipelineTraces
            .AsNoTracking()
            .Include(item => item.Steps)
            .SingleOrDefaultAsync(
                item => item.TraceId == traceId
                    && item.HospitalId == requestContext.HospitalId,
                cancellationToken);

        if (trace is null)
        {
            return null;
        }

        return new TraceResponse(
            trace.TraceId,
            trace.HospitalId,
            trace.CodingTaskId,
            trace.Status,
            trace.StartedAt,
            trace.CompletedAt,
            trace.Steps
                .OrderBy(item => item.StartedAt)
                .Select(item => new TraceStepResponse(
                    item.StepName,
                    item.Status,
                    item.StartedAt,
                    item.CompletedAt,
                    item.ErrorCode))
                .ToArray());
    }
}
