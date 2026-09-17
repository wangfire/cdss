using HospitalAi.Contracts.CodingTasks;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// 编码任务应用服务契约，具体持久化和消息发布由后续阶段实现。
/// </summary>
public interface ICodingTaskService
{
    Task<CodingTaskResponse> CreateAsync(
        CreateCodingTaskRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<CodingTaskResponse?> GetAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);
}
