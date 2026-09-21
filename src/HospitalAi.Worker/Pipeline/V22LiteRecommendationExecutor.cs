using HospitalAi.Application.Abstractions;
using HospitalAi.Contracts.CodingTasks;

namespace HospitalAi.Worker.Pipeline;

/// <summary>
/// V2.2-Lite Runner 的同步执行适配器。API 侧增量推荐直接调用，
/// 没有走 RabbitMQ，避免把“单次诊断输入重算”退化成异步全任务重跑。
/// </summary>
public sealed class V22LiteRecommendationExecutor(
    V22LiteCodingPipelineRunner runner) : IV22RecommendationExecutor
{
    public Task ExecuteAsync(
        CodingTaskCreatedMessage message,
        IReadOnlyList<Guid>? diagnosisInputIds,
        CancellationToken cancellationToken = default)
    {
        return runner.ExecuteAsync(message, diagnosisInputIds, cancellationToken);
    }
}
