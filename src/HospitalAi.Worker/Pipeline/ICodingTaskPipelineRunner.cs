using HospitalAi.Contracts.CodingTasks;

namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// 编码流水线执行器。Task7 只提供占位实现，后续接入真实编码流程。
/// </summary>
public interface ICodingTaskPipelineRunner
{
    Task RunAsync(
        CodingTaskCreatedMessage message,
        CancellationToken cancellationToken = default);
}
