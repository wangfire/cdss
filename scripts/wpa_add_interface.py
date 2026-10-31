# -*- coding: utf-8 -*-
"""WP-B：给 ICodingRecommendationService 加分页方法声明。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

import glob

f = glob.glob(r"src\HospitalAi.Application\**\ICodingRecommendationService.cs", recursive=True)[0]
c = open(f, encoding="utf-8").read()

old = """    Task<IReadOnlyList<WorkbenchTaskResponse>> ListWorkbenchTasksAsync(
        string? status,
        CancellationToken cancellationToken = default);
}"""
new = """    Task<IReadOnlyList<WorkbenchTaskResponse>> ListWorkbenchTasksAsync(
        string? status,
        CancellationToken cancellationToken = default);

    /// <summary>工作台任务服务端分页（WP-B 前端生产化）：keyword 过滤姓名/病案号。</summary>
    Task<WorkbenchTasksPageResponse> ListWorkbenchTasksPageAsync(
        string? status,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}"""
assert old in c, "interface anchor"
c = c.replace(old, new, 1)
open(f, "w", encoding="utf-8").write(c)
print("interface method added:", f)
