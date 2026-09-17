using HospitalAi.Contracts.CodingTasks;

namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// Task7 的占位流水线，验证消息闭环但不执行真实 AI 编码。
/// </summary>
public sealed class PlaceholderCodingTaskPipelineRunner : ICodingTaskPipelineRunner
{
    public Task RunAsync(
        CodingTaskCreatedMessage message,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
