using HospitalAi.Infrastructure.Outbox;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Worker.Audit;
using HospitalAi.Worker.Observability;
using HospitalAi.Worker.Pipeline;
using HospitalAi.Worker.Retry;
using HospitalAi.Worker.Tracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MassTransit;

namespace HospitalAi.Worker.Consumers;

/// <summary>
/// 消费 coding.task.created，负责 Inbox 幂等、状态推进、重试、Trace 和审计。
/// 流水线执行器由 ICodingTaskPipelineDispatcher 按任务 PipelineVersion 选择。
/// </summary>
public sealed class CodingTaskCreatedConsumer(
    HospitalAiDbContext dbContext,
    ICodingTaskPipelineDispatcher pipelineDispatcher,
    IRetryDelay retryDelay,
    ILogger<CodingTaskCreatedConsumer> logger)
    : IConsumer<CodingTaskCreatedMessage>
{
    private const string ConsumerName = "CodingTaskCreatedConsumer";
    private const string PipelineErrorCode = "PIPELINE_ERROR";
    private const int MaxAttempts = 4;

    public async Task Consume(ConsumeContext<CodingTaskCreatedMessage> context)
    {
        // TraceId 已随业务消息持久化，RabbitMQ 消费时同时放入日志作用域，便于串联 API 与 Worker。
        using var scope = logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["MessageId"] = context.Message.MessageId,
                ["TraceId"] = context.Message.TraceId,
                ["HospitalId"] = context.Message.HospitalId
            });

        await ProcessAsync(context.Message, context.CancellationToken);
    }

    public async Task ProcessAsync(
        CodingTaskCreatedMessage message,
        CancellationToken cancellationToken = default)
    {
        var inboxStore = new InboxStore(dbContext);
        var accepted = await inboxStore.TryBeginAsync(
            InboxMessage.Create(
                message.HospitalId,
                message.MessageId,
                ConsumerName),
            cancellationToken);

        if (!accepted)
        {
            return;
        }

        var task = await dbContext.CodingTasks.SingleOrDefaultAsync(
            item => item.Id == message.TaskId
                && item.HospitalId == message.HospitalId,
            cancellationToken);
        if (task is null)
        {
            await inboxStore.MarkProcessedAsync(
                message.MessageId,
                ConsumerName,
                cancellationToken);
            return;
        }

        // 根 Trace 查询必须按 hospital_id + coding_task_id + trace_id，
        // 不再假设“一个任务永远只有一条 Trace”（Full 阶段一个任务可有多次运行的多条 Trace）。
        var trace = await dbContext.PipelineTraces
            .Where(item => item.CodingTaskId == task.Id
                && item.HospitalId == task.HospitalId
                && item.TraceId == message.TraceId)
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (trace is null)
        {
            throw new InvalidOperationException("编码任务缺少 Pipeline Trace。");
        }

        var traceService = new PipelineTraceService(dbContext);
        var auditService = new AuditService(dbContext);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            ICodingTaskPipelineRunner pipelineRunner;
            try
            {
                pipelineRunner = pipelineDispatcher.Select(task.PipelineVersion);
            }
            catch (InvalidOperationException exception)
            {
                // 执行器缺失属于配置问题，不通过重试解决。
                logger.LogError(
                    exception,
                    "无法选择流水线执行器，TaskId={TaskId}, PipelineVersion={PipelineVersion}",
                    task.Id,
                    task.PipelineVersion);
                await inboxStore.MarkProcessedAsync(
                    message.MessageId,
                    ConsumerName,
                    cancellationToken);
                return;
            }

            task.Status = Domain.CodingTasks.CodingTaskStatus.Running;
            task.StartedAt ??= now;
            task.CompletedAt = null;
            task.ErrorCode = null;
            task.UpdatedAt = now;
            var step = traceService.StartAttempt(trace, attempt, now);
            await dbContext.SaveChangesAsync(cancellationToken);

            try
            {
                await pipelineRunner.RunAsync(message, cancellationToken);

                now = DateTimeOffset.UtcNow;
                if (task.Status == Domain.CodingTasks.CodingTaskStatus.Running)
                {
                    task.Status = Domain.CodingTasks.CodingTaskStatus.Success;
                }

                task.CompletedAt = now;
                if (task.Status is Domain.CodingTasks.CodingTaskStatus.Success
                    or Domain.CodingTasks.CodingTaskStatus.PendingReview)
                {
                    task.ErrorCode = null;
                }

                task.UpdatedAt = now;
                traceService.FinishAttempt(trace, step, "SUCCESS", null, now);
                auditService.Add(
                    message.HospitalId,
                    task.Id,
                    "coding_task.process",
                    "SUCCESS",
                    GetRequestId(message),
                    now);
                PipelineMetrics.RecordPipelineExecuted(message);
                PipelineMetrics.RecordProcessingCompleted("SUCCESS", message);
                await dbContext.SaveChangesAsync(cancellationToken);
                await inboxStore.MarkProcessedAsync(
                    message.MessageId,
                    ConsumerName,
                    cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                now = DateTimeOffset.UtcNow;
                task.RetryCount++;
                task.ErrorCode = PipelineErrorCode;
                task.UpdatedAt = now;
                var isFinalAttempt = attempt == MaxAttempts;
                task.Status = isFinalAttempt
                    ? Domain.CodingTasks.CodingTaskStatus.Failed
                    : Domain.CodingTasks.CodingTaskStatus.Retrying;
                task.CompletedAt = isFinalAttempt ? now : null;
                traceService.FinishAttempt(
                    trace,
                    step,
                    isFinalAttempt ? "FAILED" : "RETRYING",
                    PipelineErrorCode,
                    now);
                auditService.Add(
                    message.HospitalId,
                    task.Id,
                    "coding_task.process",
                    isFinalAttempt ? "FAILED" : "RETRY",
                    GetRequestId(message),
                    now);
                PipelineMetrics.RecordPipelineFailed(PipelineErrorCode, message);
                if (isFinalAttempt)
                {
                    PipelineMetrics.RecordProcessingCompleted("FAILED", message);
                }
                else
                {
                    PipelineMetrics.RecordProcessingCompleted("RETRY", message);
                }
                await dbContext.SaveChangesAsync(cancellationToken);

                if (isFinalAttempt)
                {
                    logger.LogError(
                        exception,
                        "编码任务处理失败，TaskId={TaskId}, TraceId={TraceId}",
                        task.Id,
                        message.TraceId);
                    await inboxStore.MarkProcessedAsync(
                        message.MessageId,
                        ConsumerName,
                        cancellationToken);
                    return;
                }

                await retryDelay.DelayAsync(
                    RetryPolicy.GetDelay(attempt),
                    cancellationToken);
            }
        }
    }

    private static string GetRequestId(CodingTaskCreatedMessage message)
    {
        return string.IsNullOrWhiteSpace(message.RequestId)
            ? message.MessageId.ToString("N")
            : message.RequestId;
    }
}
