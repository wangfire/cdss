using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Worker.Tracing;

/// <summary>
/// 维护任务级 Trace 和每次处理尝试的步骤记录。
/// </summary>
public sealed class PipelineTraceService(HospitalAiDbContext dbContext)
{
    public async Task<PipelineTraceRecord?> FindAsync(
        Guid hospitalId,
        Guid taskId,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var trace = await dbContext.PipelineTraces
            .Where(item => item.HospitalId == hospitalId
                && item.CodingTaskId == taskId
                && item.TraceId == traceId)
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (trace is not null)
        {
            return trace;
        }

        // 同一 taskId + traceId 允许存在多条历史记录（重试、并发建任务、回填脚本），
        // 取最早一条作为本次处理的 Trace，避免 Single 因多行直接抛异常。
        return dbContext.PipelineTraces.Local
            .Where(item => item.HospitalId == hospitalId
                && item.CodingTaskId == taskId
                && item.TraceId == traceId)
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefault();
    }

    public PipelineTraceStepRecord StartAttempt(
        PipelineTraceRecord trace,
        int attempt,
        DateTimeOffset now)
    {
        trace.Status = "RUNNING";
        trace.UpdatedAt = now;
        var step = new PipelineTraceStepRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = trace.HospitalId,
            PipelineTraceId = trace.Id,
            StepName = $"coding-task-attempt-{attempt}",
            Status = "RUNNING",
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.PipelineTraceSteps.Add(step);
        return step;
    }

    public void FinishAttempt(
        PipelineTraceRecord trace,
        PipelineTraceStepRecord step,
        string status,
        string? errorCode,
        DateTimeOffset now)
    {
        step.Status = status;
        step.ErrorCode = errorCode;
        step.CompletedAt = now;
        step.UpdatedAt = now;
        trace.Status = status;
        trace.CompletedAt = status is "SUCCESS" or "FAILED" ? now : null;
        trace.UpdatedAt = now;
    }
}
