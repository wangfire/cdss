using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.Outbox;

/// <summary>
/// 发布已提交的 Outbox 消息，并记录发布完成时间。
/// </summary>
public sealed class OutboxPublisher(
    HospitalAiDbContext dbContext,
    IEventBus eventBus)
{
    public async Task<int> PublishPendingAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        var messages = await dbContext.OutboxMessages
            .Where(message => message.PublishedAt == null)
            .OrderBy(message => message.OccurredAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            await eventBus.PublishAsync(
                message.Id,
                message.HospitalId,
                message.MessageType,
                message.PayloadJson,
                cancellationToken);

            message.PublishedAt = DateTimeOffset.UtcNow;
            message.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return messages.Count;
    }
}
