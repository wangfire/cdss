namespace HospitalAi.Infrastructure.Outbox;

/// <summary>
/// 消费者幂等记录的输入模型。
/// </summary>
public sealed record InboxMessage(
    Guid Id,
    Guid HospitalId,
    Guid MessageId,
    string ConsumerName,
    DateTimeOffset ReceivedAt)
{
    public static InboxMessage Create(
        Guid hospitalId,
        Guid messageId,
        string consumerName)
    {
        if (hospitalId == Guid.Empty)
        {
            throw new ArgumentException("医院标识不能为空。", nameof(hospitalId));
        }

        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("消息标识不能为空。", nameof(messageId));
        }

        if (string.IsNullOrWhiteSpace(consumerName))
        {
            throw new ArgumentException("消费者名称不能为空。", nameof(consumerName));
        }

        return new InboxMessage(
            Guid.NewGuid(),
            hospitalId,
            messageId,
            consumerName,
            DateTimeOffset.UtcNow);
    }
}
