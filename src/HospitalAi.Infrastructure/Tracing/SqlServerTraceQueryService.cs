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

        // 同一 traceId 可能对应多次尝试或多条记录，取最新一条，避免 Single 因多行直接抛异常。
        var trace = await dbContext.PipelineTraces
            .AsNoTracking()
            .Include(item => item.Steps)
            .Where(item => item.TraceId == traceId
                && item.HospitalId == requestContext.HospitalId)
            .OrderByDescending(item => item.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

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

    public async Task<IReadOnlyList<PipelineRunResponse>> ListRunsAsync(
        Guid taskId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (requestContext.HospitalId == Guid.Empty)
        {
            return [];
        }

        var traceHeads = await dbContext.PipelineTraces
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.CodingTaskId == taskId)
            .Select(item => new { item.Id, item.TraceId })
            .ToListAsync(cancellationToken);
        if (traceHeads.Count == 0)
        {
            return [];
        }

        var traceIdByHead = traceHeads.ToDictionary(item => item.Id, item => item.TraceId);
        var traceHeadIds = traceIdByHead.Keys.ToList();
        var steps = await dbContext.PipelineTraceSteps
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && traceHeadIds.Contains(item.PipelineTraceId))
            .OrderBy(item => item.StartedAt)
            .ToListAsync(cancellationToken);

        var runs = steps
            .GroupBy(item => item.PipelineRunId)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.StartedAt).ToList();
                var startedAt = ordered[0].StartedAt;
                var completedAt = ordered
                    .Select(item => item.CompletedAt)
                    .Where(item => item is not null)
                    .Select(item => item!.Value)
                    .DefaultIfEmpty(startedAt)
                    .Max();
                return new
                {
                    RunId = group.Key,
                    TraceId = traceIdByHead[ordered[0].PipelineTraceId],
                    StartedAt = startedAt,
                    CompletedAt = (DateTimeOffset?)completedAt,
                    Steps = ordered,
                };
            })
            .OrderByDescending(item => item.StartedAt)
            .Take(limit)
            .ToList();

        var runIds = runs
            .Where(item => item.RunId is not null)
            .Select(item => item.RunId!.Value)
            .ToArray();
        var recommendationStats = new Dictionary<Guid, (int Count, string? PipelineVersion)>();
        if (runIds.Length > 0)
        {
            var statsRows = await dbContext.CodingRecommendations
                .AsNoTracking()
                .Where(item => item.HospitalId == requestContext.HospitalId
                    && item.PipelineRunId != null
                    && runIds.Contains(item.PipelineRunId.Value))
                .GroupBy(item => item.PipelineRunId!.Value)
                .Select(group => new
                {
                    RunId = group.Key,
                    Count = group.Count(),
                    PipelineVersion = group.Select(item => item.PipelineVersion).FirstOrDefault(),
                })
                .ToListAsync(cancellationToken);
            foreach (var row in statsRows)
            {
                recommendationStats[row.RunId] = (row.Count, row.PipelineVersion);
            }
        }

        var issues = await dbContext.QualityIssues
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.CodingTaskId == taskId)
            .ToListAsync(cancellationToken);

        return runs
            .Select(run =>
            {
                (int Count, string? PipelineVersion)? stats = null;
                if (run.RunId is { } runId && recommendationStats.TryGetValue(runId, out var s))
                {
                    stats = s;
                }
                // quality_issue 无 run 外键，按运行时间窗归属（容差 1 分钟）。
                var runIssues = issues
                    .Where(item => item.CreatedAt >= run.StartedAt.AddMinutes(-1)
                        && item.CreatedAt <= (run.CompletedAt ?? run.StartedAt).AddMinutes(1))
                    .OrderBy(item => item.CreatedAt)
                    .Select(item => new PipelineRunIssueResponse(
                        item.IssueType.ToString(),
                        item.RiskLevel,
                        item.Description,
                        item.CurrentCode,
                        item.DiagnosisInputId))
                    .ToList();
                var degradedFlags = run.Steps
                    .Where(item => item.Status == "DEGRADED" || item.ErrorCode?.StartsWith("DEGRADED", StringComparison.Ordinal) == true)
                    .Select(item => item.ErrorCode)
                    .Where(item => item is not null)
                    .Select(item => item!)
                    .Distinct()
                    .ToList();
                return new PipelineRunResponse(
                    run.RunId,
                    run.TraceId,
                    stats?.PipelineVersion,
                    DeriveRunStatus(run.Steps, degradedFlags),
                    run.StartedAt,
                    run.CompletedAt,
                    (long)(run.CompletedAt!.Value - run.StartedAt).TotalMilliseconds,
                    degradedFlags,
                    stats?.Count ?? 0,
                    run.Steps
                        // 同一次运行内多个步骤共享 startedAt，按完成时间排序才是真实执行顺序。
                        .OrderBy(item => item.CompletedAt ?? item.StartedAt)
                        .ThenBy(item => item.StartedAt)
                        .Select(item => new PipelineRunStepResponse(
                            item.Stage,
                            item.Status,
                            item.ErrorCode,
                            item.StartedAt,
                            item.CompletedAt,
                            item.DurationMs))
                        .ToList(),
                    runIssues);
            })
            .ToList();
    }

    private static string DeriveRunStatus(
        IReadOnlyList<PipelineTraceStepRecord> steps,
        IReadOnlyList<string> degradedFlags)
    {
        var statuses = steps.Select(item => item.Status).ToHashSet(StringComparer.Ordinal);
        if (statuses.Contains("FAILED"))
        {
            return "FAILED";
        }

        if (statuses.Contains("HUMAN_REQUIRED"))
        {
            return "HUMAN_REQUIRED";
        }

        if (statuses.Contains("NO_SAFE_RECOMMENDATION"))
        {
            return "NO_SAFE_RECOMMENDATION";
        }

        return degradedFlags.Count > 0 ? "DEGRADED" : "SUCCESS";
    }
}
