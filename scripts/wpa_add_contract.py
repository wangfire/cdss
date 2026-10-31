# -*- coding: utf-8 -*-
"""WP-B：契约文件追加 WorkbenchTasksPageResponse（追加到文件尾，同 namespace）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

import glob

f = glob.glob(r"src\HospitalAi.Contracts\**\CodingRecommendationContracts.cs", recursive=True)[0]
c = open(f, encoding="utf-8").read()

if "WorkbenchTasksPageResponse" in c:
    print("already present")
    sys.exit(0)

addition = """
/// <summary>
/// 工作台任务服务端分页响应（WP-B 前端生产化）：前端不再全量拉取。
/// </summary>
public sealed record WorkbenchTasksPageResponse(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("items")] IReadOnlyList<WorkbenchTaskResponse> Items);
"""
c = c.rstrip("\n") + "\n" + addition
open(f, "w", encoding="utf-8-sig").write(c)
print("added to:", f)
