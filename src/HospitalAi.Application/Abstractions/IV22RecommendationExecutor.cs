using HospitalAi.Contracts.CodingTasks;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// 对指定诊断输入执行 V2.2 推荐流水线的执行器抽象。
/// 由 Worker 侧 V2.2-Lite Runner 适配实现，避免 Infrastructure 反向依赖 Worker 程序集。
/// </summary>
public interface IV22RecommendationExecutor
{
    Task ExecuteAsync(
        CodingTaskCreatedMessage message,
        IReadOnlyList<Guid>? diagnosisInputIds,
        CancellationToken cancellationToken = default);
}
