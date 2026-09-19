using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Contracts.CodingRecommendations;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.CodingRecommendations;

/// <summary>
/// 基于 SQL Server 的编码推荐查询与人工审核服务。
/// </summary>
public sealed class SqlServerCodingRecommendationService(
    HospitalAiDbContext dbContext,
    IRequestContext requestContext) : ICodingRecommendationService
{
    public async Task<CodingTaskRecommendationsResponse?> GetRecommendationsAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var task = await dbContext.CodingTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == taskId
                    && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (task is null)
        {
            return null;
        }

        var recommendations = await dbContext.CodingRecommendations
            .AsNoTracking()
            .Include(item => item.Evidences)
            .Where(item => item.CodingTaskId == taskId && item.HospitalId == requestContext.HospitalId)
            .OrderBy(item => item.RecommendationType)
            .ThenBy(item => item.Rank)
            .Select(item => new CodingRecommendationResponse(
                item.Id,
                item.RecommendationType,
                item.CodeSystemCode,
                item.Code,
                item.Title,
                item.Rank,
                item.ConfidenceScore,
                item.ReviewStatus,
                item.Evidences
                    .OrderBy(evidence => evidence.Score)
                    .Select(evidence => new RecommendationEvidenceResponse(
                        evidence.SourceType,
                        evidence.SourceText,
                        evidence.MatchText,
                        evidence.Score))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new CodingTaskRecommendationsResponse(
            task.Id,
            task.Status.ToWireValue(),
            recommendations);
    }

    public async Task<CodingReviewResponse> ReviewAsync(
        Guid taskId,
        ReviewCodingTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var status = NormalizeReviewStatus(request.ReviewStatus);
        var task = await dbContext.CodingTasks.SingleOrDefaultAsync(
            item => item.Id == taskId
                && item.HospitalId == requestContext.HospitalId,
            cancellationToken);
        if (task is null)
        {
            throw new ResourceNotFoundException("编码任务不存在。");
        }

        await EnsureRecommendationsBelongToTaskAsync(taskId, request, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        dbContext.CodingReviews.Add(new CodingReviewRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = requestContext.HospitalId,
            CodingTaskId = taskId,
            ReviewStatus = status,
            Comment = request.Comment,
            ReviewerId = requestContext.UserId,
            ReviewedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        var existingFinals = await dbContext.FinalCodingResults
            .Where(item => item.HospitalId == requestContext.HospitalId && item.CodingTaskId == taskId)
            .ToListAsync(cancellationToken);
        dbContext.FinalCodingResults.RemoveRange(existingFinals);

        foreach (var item in request.FinalCodes)
        {
            dbContext.FinalCodingResults.Add(new FinalCodingResultRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = requestContext.HospitalId,
                CodingTaskId = taskId,
                SourceRecommendationId = item.RecommendationId,
                ResultType = item.ResultType,
                CodeSystemCode = item.CodeSystem,
                Code = item.Code,
                Title = item.Title,
                ReviewerId = requestContext.UserId,
                ConfirmedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await dbContext.CodingRecommendations
            .Where(item => item.HospitalId == requestContext.HospitalId && item.CodingTaskId == taskId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.ReviewStatus, status)
                    .SetProperty(item => item.UpdatedAt, now),
                cancellationToken);

        task.Status = status switch
        {
            "ACCEPTED" => CodingTaskStatus.Accepted,
            "MODIFIED" => CodingTaskStatus.Modified,
            "REJECTED" => CodingTaskStatus.Rejected,
            "HUMAN_REQUIRED" => CodingTaskStatus.HumanRequired,
            _ => CodingTaskStatus.PendingReview
        };
        task.CompletedAt = now;
        task.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CodingReviewResponse(taskId, status, request.FinalCodes.Count);
    }

    public async Task<IReadOnlyList<WorkbenchTaskResponse>> ListWorkbenchTasksAsync(
        string? status,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var query = dbContext.CodingTasks
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId);

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<CodingTaskStatus>(ToPascalStatus(status), true, out var parsed))
        {
            query = query.Where(item => item.Status == parsed);
        }

        var records = await query
            .OrderByDescending(item => item.CreatedAt)
            .Take(100)
            .Select(item => new
            {
                item.Id,
                item.VisitId,
                item.Status,
                RecommendationCount = dbContext.CodingRecommendations.Count(
                    recommendation => recommendation.CodingTaskId == item.Id),
                item.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return records
            .Select(item => new WorkbenchTaskResponse(
                item.Id,
                item.VisitId,
                item.Status.ToWireValue(),
                item.RecommendationCount,
                item.CreatedAt))
            .ToList();
    }

    private static string NormalizeReviewStatus(string status)
    {
        var normalized = status.Trim().ToUpperInvariant();
        return normalized switch
        {
            "ACCEPTED" or "MODIFIED" or "REJECTED" or "HUMAN_REQUIRED" or "PENDING_REVIEW" => normalized,
            _ => throw new ValidationException("审核状态不合法。")
        };
    }

    private static string ToPascalStatus(string status)
    {
        return status.Trim().ToUpperInvariant() switch
        {
            "PENDING" => nameof(CodingTaskStatus.Pending),
            "RUNNING" => nameof(CodingTaskStatus.Running),
            "SUCCESS" => nameof(CodingTaskStatus.Success),
            "FAILED" => nameof(CodingTaskStatus.Failed),
            "RETRYING" => nameof(CodingTaskStatus.Retrying),
            "TIMEOUT" => nameof(CodingTaskStatus.Timeout),
            "CANCELLED" => nameof(CodingTaskStatus.Cancelled),
            "HUMAN_REQUIRED" => nameof(CodingTaskStatus.HumanRequired),
            "PENDING_REVIEW" => nameof(CodingTaskStatus.PendingReview),
            "ACCEPTED" => nameof(CodingTaskStatus.Accepted),
            "MODIFIED" => nameof(CodingTaskStatus.Modified),
            "REJECTED" => nameof(CodingTaskStatus.Rejected),
            _ => status
        };
    }

    private async Task EnsureRecommendationsBelongToTaskAsync(
        Guid taskId,
        ReviewCodingTaskRequest request,
        CancellationToken cancellationToken)
    {
        var recommendationIds = request.FinalCodes
            .Select(item => item.RecommendationId)
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .Distinct()
            .ToList();
        if (recommendationIds.Count == 0)
        {
            return;
        }

        var matchedCount = await dbContext.CodingRecommendations.CountAsync(
            item => item.HospitalId == requestContext.HospitalId
                && item.CodingTaskId == taskId
                && recommendationIds.Contains(item.Id),
            cancellationToken);
        if (matchedCount != recommendationIds.Count)
        {
            throw new ValidationException("最终编码引用的推荐不属于当前编码任务。");
        }
    }

    private void EnsureHospital()
    {
        if (requestContext.HospitalId == Guid.Empty)
        {
            throw new ValidationException("X-Hospital-Id 不能为空。");
        }
    }
}
