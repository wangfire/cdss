namespace HospitalAi.Infrastructure.Outbox;

/// <summary>
/// 待发布的 Outbox 消息。
/// </summary>
public sealed record OutboxMessage(
    Guid Id,
    Guid HospitalId,
    string MessageType,
    string PayloadJson,
    DateTimeOffset OccurredAt)
{
    public static OutboxMessage Create(
        Guid hospitalId,
        string messageType,
        string payloadJson)
    {
        return Create(Guid.NewGuid(), hospitalId, messageType, payloadJson);
    }

    /// <summary>
    /// 使用调用方指定的消息 ID 创建 Outbox，确保消息体和持久化记录使用同一个 ID。
    /// </summary>
    public static OutboxMessage Create(
        Guid messageId,
        Guid hospitalId,
        string messageType,
        string payloadJson)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("消息标识不能为空。", nameof(messageId));
        }

        if (hospitalId == Guid.Empty)
        {
            throw new ArgumentException("医院标识不能为空。", nameof(hospitalId));
        }

        if (string.IsNullOrWhiteSpace(messageType))
        {
            throw new ArgumentException("消息类型不能为空。", nameof(messageType));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException("消息负载不能为空。", nameof(payloadJson));
        }

        return new OutboxMessage(
            messageId,
            hospitalId,
            messageType,
            payloadJson,
            DateTimeOffset.UtcNow);
    }
}
