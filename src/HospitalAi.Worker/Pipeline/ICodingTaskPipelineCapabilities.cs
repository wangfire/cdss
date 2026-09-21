using HospitalAi.Contracts.CodingTasks;

namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// 编码流水线执行器的能力声明。Dispatcher 只依据声明的 PipelineVersion 选择 Runner，
/// 不判断具体类型：新增 V2.2 家族实现时不会因为漏配而静默走旧逻辑。
/// </summary>
public interface ICodingTaskPipelineCapabilities
{
    /// <summary>本执行器声明负责处理的 PipelineVersion 列表。</summary>
    IReadOnlyCollection<string> SupportedPipelineVersions { get; }
}
