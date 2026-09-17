namespace HospitalAi.Infrastructure.Outbox;

/// <summary>
/// 基础事件总线抽象，后续由 RabbitMQ/MassTransit 适配器实现。
/// </summary>
public interface IEventBus
{
    Task PublishAsync(
        Guid messageId,
        Guid hospitalId,
        string messageType,
        string payloadJson,
        CancellationToken cancellationToken = default);
}
