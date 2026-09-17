using System.Text.Json;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Infrastructure.Outbox;
using MassTransit;

namespace HospitalAi.Infrastructure.RabbitMq;

/// <summary>
/// 将 Outbox 记录转换为共享消息契约并交给 MassTransit 发布。
/// </summary>
public sealed class RabbitMqEventBus(
    IPublishEndpoint publishEndpoint) : IEventBus
{
    public async Task PublishAsync(
        Guid messageId,
        Guid hospitalId,
        string messageType,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        var message = ToCodingTaskCreatedMessage(messageId, messageType, payloadJson);
        if (message.HospitalId != hospitalId)
        {
            throw new InvalidOperationException("Outbox 消息的医院标识与消息体不一致。");
        }

        await publishEndpoint.Publish(
            message,
            publishContext =>
            {
                publishContext.MessageId = messageId;
                publishContext.Headers.Set("trace-id", message.TraceId);
            },
            cancellationToken);
    }

    public static object ToBrokerMessage(
        string messageType,
        string payloadJson)
    {
        return ToCodingTaskCreatedMessage(null, messageType, payloadJson);
    }

    public static object ToBrokerMessage(
        Guid messageId,
        string messageType,
        string payloadJson)
    {
        return ToCodingTaskCreatedMessage(messageId, messageType, payloadJson);
    }

    private static CodingTaskCreatedMessage ToCodingTaskCreatedMessage(
        Guid? messageId,
        string messageType,
        string payloadJson)
    {
        if (!string.Equals(
                messageType,
                "coding.task.created",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"未知 Outbox 消息类型：{messageType}");
        }

        var message = JsonSerializer.Deserialize<CodingTaskCreatedMessage>(
            payloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (message is null)
        {
            throw new InvalidOperationException("编码任务创建消息负载无效。");
        }

        if (messageId.HasValue && message.MessageId != messageId.Value)
        {
            throw new InvalidOperationException("Outbox 消息标识不一致。");
        }

        return message;
    }
}
