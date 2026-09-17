using System.Text.Json;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Infrastructure.RabbitMq;

namespace HospitalAi.Infrastructure.Tests;

public sealed class RabbitMqEventBusTests
{
    [Fact]
    public void ToBrokerMessage_将编码任务创建事件反序列化为共享契约()
    {
        var messageId = Guid.NewGuid();
        var hospitalId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var source = new CodingTaskCreatedMessage(
            messageId,
            hospitalId,
            taskId,
            visitId,
            "pipeline-v1",
            "trace-rabbit");

        var converted = RabbitMqEventBus.ToBrokerMessage(
            "coding.task.created",
            JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var message = Assert.IsType<CodingTaskCreatedMessage>(converted);
        Assert.Equal(messageId, message.MessageId);
        Assert.Equal(hospitalId, message.HospitalId);
        Assert.Equal(taskId, message.TaskId);
        Assert.Equal(visitId, message.VisitId);
        Assert.Equal("pipeline-v1", message.PipelineVersion);
        Assert.Equal("trace-rabbit", message.TraceId);
    }

    [Fact]
    public void ToBrokerMessage_未知消息类型直接拒绝()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RabbitMqEventBus.ToBrokerMessage("unknown.event", "{}"));

        Assert.Contains("未知 Outbox 消息类型", exception.Message);
    }

    [Fact]
    public void ToBrokerMessage_消息体Id与OutboxId不一致时拒绝()
    {
        var source = new CodingTaskCreatedMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "pipeline-v1",
            "trace-rabbit-mismatch");

        var exception = Assert.Throws<InvalidOperationException>(
            () => RabbitMqEventBus.ToBrokerMessage(
                Guid.NewGuid(),
                "coding.task.created",
                JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        Assert.Contains("消息标识不一致", exception.Message);
    }
}
