using HospitalAi.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.Outbox;

/// <summary>
/// Inbox 存储，保证相同消息不会被同一个消费者重复开始处理。
/// </summary>
public sealed class InboxStore(HospitalAiDbContext dbContext)
{
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);

    public async Task<bool> TryBeginAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.InboxMessages.SingleOrDefaultAsync(
            item => item.MessageId == message.MessageId
                && item.ConsumerName == message.ConsumerName,
            cancellationToken);

        if (existing?.ProcessedAt is not null)
        {
            return false;
        }

        if (existing is not null)
        {
            var leaseNow = DateTimeOffset.UtcNow;
            if (existing.ReceivedAt > leaseNow - ProcessingLease)
            {
                return false;
            }

            // 未完成且租约过期的 Inbox 视为上次处理异常中断，允许 Worker 重新领取。
            existing.ReceivedAt = leaseNow;
            existing.UpdatedAt = leaseNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        dbContext.InboxMessages.Add(new InboxMessageRecord
        {
            Id = message.Id,
            HospitalId = message.HospitalId,
            MessageId = message.MessageId,
            ConsumerName = message.ConsumerName,
            ReceivedAt = message.ReceivedAt,
            CreatedAt = now,
            UpdatedAt = now
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.Entry(
                    dbContext.InboxMessages.Local.Single(
                        item => item.Id == message.Id))
                .State = EntityState.Detached;
            return false;
        }
    }

    public async Task MarkProcessedAsync(
        Guid messageId,
        string consumerName,
        CancellationToken cancellationToken = default)
    {
        var record = await dbContext.InboxMessages.SingleAsync(
            item => item.MessageId == messageId
                && item.ConsumerName == consumerName,
            cancellationToken);

        record.ProcessedAt = DateTimeOffset.UtcNow;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException
            && (sqlException.Number == 2601 || sqlException.Number == 2627);
    }
}
