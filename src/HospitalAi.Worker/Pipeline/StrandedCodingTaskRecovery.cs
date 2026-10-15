using System.Text.Json;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.Outbox;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// 终态写回补偿。进程在"流水线跑完 → 写终态"之间被中断时，任务会永久停在 Running：
/// MQ 消息已 ack 不会再来，进程内派发器又只认领 CodingStage=Imported 的行
/// （中断的任务早已进入 AiRecommending）。Worker 启动时把这些遗留行重新投递到 Outbox，
/// 交回正常消费链路重跑。
/// 认领用条件 UPDATE（Running → Pending），多实例同时启动也只有一个能重投；
/// MessageId 必须新生成（inbox 按 MessageId 去重，重放原消息会被消费者静默跳过），
/// TraceId 必须复用（消费者按任务 + TraceId 定位根 Trace，新值会被判为缺少 Trace）。
/// </summary>
public sealed class StrandedCodingTaskRecovery(
    IServiceScopeFactory scopeFactory,
    ILogger<StrandedCodingTaskRecovery> logger) : IHostedService
{
    /// <summary>与进程内派发器同口径：Running 超过该时长视为上一次进程中断遗留。</summary>
    private static readonly TimeSpan StaleRunningThreshold = TimeSpan.FromMinutes(10);

    private const int BatchSize = 200;

    private const string MessageType = "coding.task.created";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();
        var requeued = await RecoverStrandedBatchAsync(dbContext, cancellationToken);
        if (requeued > 0)
        {
            logger.LogWarning("启动补偿：{Requeued} 个中断遗留的 Running 编码任务已重新入队。", requeued);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>重投一批遗留 Running 任务。公开供集成测试直调。</summary>
    public async Task<int> RecoverStrandedBatchAsync(
        HospitalAiDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var staleBefore = DateTimeOffset.UtcNow - StaleRunningThreshold;
        var candidates = await dbContext.CodingTasks
            .AsNoTracking()
            .Where(item => item.PipelineVersion == PipelineVersions.Lite
                && item.Status == CodingTaskStatus.Running
                && item.UpdatedAt < staleBefore)
            .OrderBy(item => item.CreatedAt)
            .Take(BatchSize)
            .Select(item => new { item.Id, item.HospitalId, item.VisitId })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var requeued = 0;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var claimed = await dbContext.CodingTasks
                    .Where(item => item.Id == candidate.Id
                        && item.Status == CodingTaskStatus.Running
                        && item.UpdatedAt < staleBefore)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(item => item.Status, CodingTaskStatus.Pending)
                            .SetProperty(item => item.ErrorCode, (string?)null)
                            .SetProperty(item => item.UpdatedAt, now),
                        cancellationToken);
                if (claimed == 0)
                {
                    continue;
                }

                var traceId = await dbContext.PipelineTraces
                    .Where(item => item.CodingTaskId == candidate.Id
                        && item.HospitalId == candidate.HospitalId)
                    .OrderBy(item => item.CreatedAt)
                    .Select(item => item.TraceId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(traceId))
                {
                    // 缺根 Trace 属于数据缺陷，重投只会无限回放，与派发器一致直接落 Failed 终态。
                    await dbContext.CodingTasks
                        .Where(item => item.Id == candidate.Id)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(item => item.Status, CodingTaskStatus.Failed)
                                .SetProperty(item => item.ErrorCode, "NO_ROOT_TRACE")
                                .SetProperty(item => item.CompletedAt, (DateTimeOffset?)now)
                                .SetProperty(item => item.UpdatedAt, now),
                            cancellationToken);
                    logger.LogError("编码任务缺少根 Trace，已置为 Failed。TaskId={TaskId}", candidate.Id);
                    continue;
                }

                var message = new CodingTaskCreatedMessage(
                    MessageId: Guid.NewGuid(),
                    HospitalId: candidate.HospitalId,
                    TaskId: candidate.Id,
                    VisitId: candidate.VisitId,
                    PipelineVersion: PipelineVersions.Lite,
                    TraceId: traceId);
                new OutboxStore(dbContext).Add(OutboxMessage.Create(
                    message.MessageId,
                    candidate.HospitalId,
                    MessageType,
                    JsonSerializer.Serialize(message, PayloadOptions)));
                requeued++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return requeued;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
