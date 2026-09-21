using HospitalAi.Contracts.CodingRecommendations;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// 编码推荐查询与人工审核服务。
/// </summary>
public interface ICodingRecommendationService
{
    /// <summary>
    /// 查询编码任务的推荐结果。默认只返回当前 PipelineVersion 的结果；
    /// includeLegacy 为 true 时才显式包含 Legacy / 失效的历史推荐。
    /// </summary>
    Task<CodingTaskRecommendationsResponse?> GetRecommendationsAsync(
        Guid taskId,
        bool includeLegacy = false,
        CancellationToken cancellationToken = default);

    Task<CodingReviewResponse> ReviewAsync(
        Guid taskId,
        ReviewCodingTaskRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkbenchTaskResponse>> ListWorkbenchTasksAsync(
        string? status,
        CancellationToken cancellationToken = default);
}
