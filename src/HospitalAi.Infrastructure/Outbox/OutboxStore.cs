using HospitalAi.Infrastructure.SqlServer;

namespace HospitalAi.Infrastructure.Outbox;

/// <summary>
/// 将 Outbox 消息加入当前工作单元，调用方负责和业务数据一起提交事务。
/// </summary>
public sealed class OutboxStore(HospitalAiDbContext dbContext)
{
    public void Add(OutboxMessage message)
    {
        var now = DateTimeOffset.UtcNow;

        dbContext.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = message.Id,
            HospitalId = message.HospitalId,
            MessageType = message.MessageType,
            PayloadJson = message.PayloadJson,
            OccurredAt = message.OccurredAt,
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
