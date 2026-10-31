# -*- coding: utf-8 -*-
"""WP-B：服务实现 ListWorkbenchTasksPageAsync（服务端 keyword/page/pageSize）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

import glob

f = glob.glob(r"src\HospitalAi.Infrastructure\**\SqlServerCodingRecommendationService.cs", recursive=True)[0]
c = open(f, encoding="utf-8").read()

anchor = """        return records
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
    }"""

new_method = anchor + """

    /// <summary>
    /// 工作台任务服务端分页（WP-B 前端生产化）：keyword 在数据库端过滤姓名/病案号，
    /// 前端不再全量拉取。pageSize 上限 100。
    /// </summary>
    public async Task<WorkbenchTasksPageResponse> ListWorkbenchTasksPageAsync(
        string? status,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.CodingTasks
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId);

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<CodingTaskStatus>(ToPascalStatus(status), true, out var parsed))
        {
            query = query.Where(item => item.Status == parsed);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{keyword.Trim()}%";
            query = query.Where(item =>
                EF.Functions.Like(item.Visit.Patient.DisplayName, pattern)
                || EF.Functions.Like(item.Visit.Patient.SourcePatientId, pattern));
        }

        var total = await query.CountAsync(cancellationToken);

        var records = await query
            .OrderByDescending(item => item.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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

        var items = records
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

        return new WorkbenchTasksPageResponse(total, page, pageSize, items);
    }"""

assert anchor in c, "impl anchor"
c = c.replace(anchor, new_method, 1)
open(f, "w", encoding="utf-8").write(c)
print("paged impl added:", f)
