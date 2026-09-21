namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// 流水线分发器：按任务 PipelineVersion 选择 Runner。
/// 新任务默认走配置的 phase1-v2.2-lite；旧任务只用于历史回放；
/// 不允许因为模型不可用回退到 Legacy Runner。
/// </summary>
public interface ICodingTaskPipelineDispatcher
{
    ICodingTaskPipelineRunner Select(string pipelineVersion);
}
