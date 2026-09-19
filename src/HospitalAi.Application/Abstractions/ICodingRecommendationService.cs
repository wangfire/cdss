using HospitalAi.Contracts.CodingRecommendations;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// 编码推荐查询与人工审核服务。
/// </summary>
public interface ICodingRecommendationService
{
    Task<CodingTaskRecommendationsResponse?> GetRecommendationsAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);

    Task<CodingReviewResponse> ReviewAsync(
        Guid taskId,
        ReviewCodingTaskRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkbenchTaskResponse>> ListWorkbenchTasksAsync(
        string? status,
        CancellationToken cancellationToken = default);
}
