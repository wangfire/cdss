# -*- coding: utf-8 -*-
"""后端：GET /api/v1/workbench/tasks/stats（状态分组计数，仪表盘统计卡）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

# 1. Contracts：TaskStatsResponse
fc = r"src\HospitalAi.Contracts\CodingRecommendations\CodingRecommendationContracts.cs"
c = open(fc, encoding="utf-8").read()
if "WorkbenchTaskStatsResponse" not in c:
    addition = """
/// <summary>
/// 工作台任务状态统计（仪表盘统计卡）：按医院隔离的状态分组计数。
/// </summary>
public sealed record WorkbenchTaskStatsResponse(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("byStatus")] IReadOnlyList<TaskStatusCount> ByStatus);

public sealed record TaskStatusCount(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("count")] int Count);
"""
    c = c.rstrip("\n") + "\n" + addition
    open(fc, "w", encoding="utf-8-sig").write(c)
    print("contracts: WorkbenchTaskStatsResponse added")

# 2. 接口声明
f2 = r"src\HospitalAi.Application\Abstractions\ICodingRecommendationService.cs"
c2 = open(f2, encoding="utf-8").read()
if "GetWorkbenchTaskStatsAsync" not in c2:
    anchor = """    /// <summary>工作台任务服务端分页（WP-B 前端生产化）：keyword 过滤姓名/病案号。</summary>
    Task<WorkbenchTasksPageResponse> ListWorkbenchTasksPageAsync(
        string? status,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}"""
    new = """    /// <summary>工作台任务服务端分页（WP-B 前端生产化）：keyword 过滤姓名/病案号。</summary>
    Task<WorkbenchTasksPageResponse> ListWorkbenchTasksPageAsync(
        string? status,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>工作台任务状态统计（仪表盘统计卡）：按医院隔离的状态分组计数。</summary>
    Task<WorkbenchTaskStatsResponse> GetWorkbenchTaskStatsAsync(
        CancellationToken cancellationToken = default);
}"""
    assert anchor in c2, "iface anchor"
    c2 = c2.replace(anchor, new, 1)
    open(f2, "w", encoding="utf-8").write(c2)
    print("interface: GetWorkbenchTaskStatsAsync added")

# 3. 实现
import glob
f3 = glob.glob(r"src\HospitalAi.Infrastructure\**\SqlServerCodingRecommendationService.cs", recursive=True)[0]
c3 = open(f3, encoding="utf-8").read()
if "GetWorkbenchTaskStatsAsync" not in c3:
    anchor3 = """        return new WorkbenchTasksPageResponse(total, page, pageSize, items);
    }"""
    new3 = anchor3 + """

    /// <summary>
    /// 工作台任务状态统计（仪表盘统计卡）：数据库端 GROUP BY 状态，避免全量拉取。
    /// </summary>
    public async Task<WorkbenchTaskStatsResponse> GetWorkbenchTaskStatsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var grouped = await dbContext.CodingTasks
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId)
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var byStatus = grouped
            .Select(item => new TaskStatusCount(item.Status.ToWireValue(), item.Count))
            .ToList();

        return new WorkbenchTaskStatsResponse(byStatus.Sum(item => item.Count), byStatus);
    }"""
    assert anchor3 in c3, "impl anchor"
    c3 = c3.replace(anchor3, new3, 1)
    open(f3, "w", encoding="utf-8").write(c3)
    print("impl: GetWorkbenchTaskStatsAsync added")

# 4. 端点（插在 workbench/tasks GET 之后）
p4 = r"src\HospitalAi.Api\Program.cs"
c4 = open(p4, encoding="utf-8-sig").read()
if "/workbench/tasks/stats" not in c4:
    anchor4 = """        var response = await service.ListWorkbenchTasksPageAsync(
            status, keyword, page ?? 1, pageSize ?? 20, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);"""
    new4 = anchor4 + """

// 工作台任务状态统计（仪表盘统计卡）：数据库端 GROUP BY，无全量拉取。
app.MapGet(
    "/api/v1/workbench/tasks/stats",
    async (ICodingRecommendationService service, CancellationToken cancellationToken) =>
    {
        var response = await service.GetWorkbenchTaskStatsAsync(cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);"""
    assert anchor4 in c4, "endpoint anchor"
    c4 = c4.replace(anchor4, new4, 1)
    open(p4, "w", encoding="utf-8-sig").write(c4)
    print("endpoint: /workbench/tasks/stats added")
