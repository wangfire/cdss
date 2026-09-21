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
    /// <summary>
    /// 非当前活跃状态的推荐：默认查询不返回，必须显式 includeLegacy。
    /// </summary>
    private static readonly string[] LegacyRecommendationLifecycleStatuses =
        ["STALE", "LEGACY_READ_ONLY", "SUPERSEDED"];

    public async Task<CodingTaskRecommendationsResponse?> GetRecommendationsAsync(
        Guid taskId,
        bool includeLegacy = false,
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

        var activeRows = await dbContext.CodingRecommendations
            .AsNoTracking()
            .Include(item => item.Evidences)
            .Where(item => item.CodingTaskId == taskId
                && item.HospitalId == requestContext.HospitalId
                && item.PipelineVersion == task.PipelineVersion
                && (includeLegacy || !LegacyRecommendationLifecycleStatuses.Contains(item.LifecycleStatus)))
            .OrderBy(item => item.RecommendationType)
            .ThenBy(item => item.Rank)
            .ToListAsync(cancellationToken);

        // 同一诊断输入的同一编码可能因历史重跑并存多条 ACTIVE（已确认的旧版不会被软失效），
        // 读取端按输入+编码去重，避免工作台出现重复卡片。
        var deduped = activeRows
            .GroupBy(item => (item.DiagnosisInputId, item.CodeSystemCode, item.Code))
            .Select(group => group
                .OrderByDescending(HasConfirmedReview)
                .ThenByDescending(VersionNumber)
                .First())
            .ToList();

        // 一次取回本任务涉及的诊断输入元数据（原文 / 主诊断标志 / 导入顺序 / 来源类型），
        // 供前端分组排序与"入院诊断"页签分类使用。
        var inputIds = deduped
            .Select(item => item.DiagnosisInputId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
        var inputMap = (await dbContext.CodingDiagnosisInputs
            .AsNoTracking()
            .Where(input => inputIds.Contains(input.Id))
            .Select(input => new { input.Id, input.OriginalText, input.IsPrincipal, input.DiagnosisOrder, input.SourceType })
            .ToListAsync(cancellationToken))
            .ToDictionary(item => item.Id);

        var recommendations = deduped
            .Select(item =>
            {
                var input = item.DiagnosisInputId is { } inputId && inputMap.TryGetValue(inputId, out var found)
                    ? found
                    : null;
                return new CodingRecommendationResponse(
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
                        .ToList(),
                    item.DiagnosisInputId,
                    input?.OriginalText,
                    input is null ? null : input.IsPrincipal,
                    input?.DiagnosisOrder,
                    input?.SourceType);
            })
            .ToList();

        return new CodingTaskRecommendationsResponse(
            task.Id,
            task.Status.ToWireValue(),
            recommendations);
    }

    private static bool HasConfirmedReview(CodingRecommendationRecord item) =>
        item.ReviewStatus is "ACCEPTED" or "MODIFIED";

    private static int VersionNumber(CodingRecommendationRecord item)
    {
        var version = item.RecommendationVersion;
        if (string.IsNullOrEmpty(version))
        {
            return -1;
        }

        var separator = version.LastIndexOf('-');
        return separator >= 0 && int.TryParse(version.AsSpan(separator + 1), out var index)
            ? index
            : -1;
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
            ResultType = status,
            CreatedAt = now,
            UpdatedAt = now
        });

        // 只替换本次请求显式覆盖的最终编码：未在请求中列出的已确认 Final Coding 必须保留，
        // 已确认结果不允许被整表清空后重写。
        var requestedSourceIds = request.FinalCodes
            .Where(item => item.RecommendationId.HasValue)
            .Select(item => item.RecommendationId!.Value)
            .ToHashSet();
        var existingFinals = await dbContext.FinalCodingResults
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.CodingTaskId == taskId
                && item.SourceRecommendationId.HasValue
                && requestedSourceIds.Contains(item.SourceRecommendationId.Value))
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

        // 前端按姓名/病案号做客户端过滤，需一次拉全量任务；上限只防失控增长。
        var records = await query
            .OrderByDescending(item => item.CreatedAt)
            .Take(5000)
            .Select(item => new
            {
                item.Id,
                item.VisitId,
                item.Status,
                RecommendationCount = dbContext.CodingRecommendations
                    .Where(recommendation => recommendation.CodingTaskId == item.Id
                        && !LegacyRecommendationLifecycleStatuses.Contains(recommendation.LifecycleStatus))
                    .Select(recommendation => new
                    {
                        recommendation.DiagnosisInputId,
                        recommendation.CodeSystemCode,
                        recommendation.Code
                    })
                    .Distinct()
                    .Count(),
                item.CreatedAt,
                PatientName = item.Visit.Patient.DisplayName,
                MedicalRecordNo = item.Visit.Patient.SourcePatientId,
                AdmissionCount = dbContext.Visits.Count(
                    v => v.PatientId == item.Visit.PatientId),
                item.Visit.DischargeAt
            })
            .ToListAsync(cancellationToken);

        return records
            .Select(item => new WorkbenchTaskResponse(
                item.Id,
                item.VisitId,
                item.Status.ToWireValue(),
                item.RecommendationCount,
                item.CreatedAt,
                item.PatientName,
                item.MedicalRecordNo,
                item.AdmissionCount,
                item.DischargeAt))
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
