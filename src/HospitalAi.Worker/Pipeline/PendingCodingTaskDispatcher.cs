using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker.Consumers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// RabbitMQ 禁用（本地/单机部署）时的进程内兜底派发通道：轮询 Pending 编码任务，
/// 复用 CodingTaskCreatedConsumer 的完整处理逻辑（Inbox 幂等、状态机、重试、Trace、审计）。
/// 与 MQ 消费者按 RabbitMq:Enabled 配置互斥注册，消除"HTTP 请求内同步执行"与
/// "队列路径"的双路径并发（WP-B2-3 决策 1）；认领用条件 UPDATE（仅 Pending 或
/// 超时僵死的 Running 可置 Running），即使配置误配也不会重复执行同一任务。
/// </summary>
public sealed class PendingCodingTaskDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<PendingCodingTaskDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Running 超过该时长视为进程中断遗留的僵死任务，重新认领执行。</summary>
    private static readonly TimeSpan StaleRunningThreshold = TimeSpan.FromMinutes(10);

    private const int BatchSize = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();
                var consumer = scope.ServiceProvider.GetRequiredService<CodingTaskCreatedConsumer>();
                await DispatchPendingBatchAsync(dbContext, consumer, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "进程内派发 Pending 编码任务失败，将在下一轮重试。");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    /// <summary>派发一批 Pending（或僵死 Running）任务。公开供集成测试直调。</summary>
    public async Task DispatchPendingBatchAsync(
        HospitalAiDbContext dbContext,
        CodingTaskCreatedConsumer consumer,
        CancellationToken cancellationToken = default)
    {
        var staleBefore = DateTimeOffset.UtcNow - StaleRunningThreshold;
        var pendingTasks = await dbContext.CodingTasks
            .Where(item => item.PipelineVersion == PipelineVersions.Lite
                && item.CodingStage == CodingStage.Imported
                && (item.Status == CodingTaskStatus.Pending
                    || (item.Status == CodingTaskStatus.Running && item.UpdatedAt < staleBefore)))
            .OrderBy(item => item.CreatedAt)
            .Take(BatchSize)
            .Select(item => new { item.Id, item.HospitalId, item.VisitId })
            .ToListAsync(cancellationToken);
        if (pendingTasks.Count == 0)
        {
            return;
        }

        foreach (var pendingTask in pendingTasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            // 原子认领：条件 UPDATE 防止多通道/多进程并发执行同一任务。
            var claimed = await dbContext.CodingTasks
                .Where(item => item.Id == pendingTask.Id
                    && (item.Status == CodingTaskStatus.Pending
                        || (item.Status == CodingTaskStatus.Running && item.UpdatedAt < staleBefore)))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.Status, CodingTaskStatus.Running)
                        .SetProperty(item => item.StartedAt, (DateTimeOffset?)now)
                        .SetProperty(item => item.UpdatedAt, now),
                    cancellationToken);
            if (claimed == 0)
            {
                continue;
            }

            var traceId = await dbContext.PipelineTraces
                .Where(item => item.CodingTaskId == pendingTask.Id
                    && item.HospitalId == pendingTask.HospitalId)
                .OrderBy(item => item.CreatedAt)
                .Select(item => item.TraceId)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(traceId))
            {
                // 认领后才发现数据缺陷：置 Failed 终态，避免每轮重复认领。
                var brokenAt = DateTimeOffset.UtcNow;
                await dbContext.CodingTasks
                    .Where(item => item.Id == pendingTask.Id)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(item => item.Status, CodingTaskStatus.Failed)
                            .SetProperty(item => item.ErrorCode, "NO_ROOT_TRACE")
                            .SetProperty(item => item.CompletedAt, (DateTimeOffset?)brokenAt)
                            .SetProperty(item => item.UpdatedAt, brokenAt),
                        cancellationToken);
                logger.LogError("编码任务缺少根 Trace，已置为 Failed。TaskId={TaskId}", pendingTask.Id);
                continue;
            }

            logger.LogInformation(
                "进程内派发编码任务，TaskId={TaskId}, TraceId={TraceId}",
                pendingTask.Id,
                traceId);
            var message = new CodingTaskCreatedMessage(
                MessageId: Guid.NewGuid(),
                HospitalId: pendingTask.HospitalId,
                TaskId: pendingTask.Id,
                VisitId: pendingTask.VisitId,
                PipelineVersion: PipelineVersions.Lite,
                TraceId: traceId);
            await consumer.ProcessAsync(message, cancellationToken);
        }
    }
}
