# -*- coding: utf-8 -*-
"""前端：workbench.ts 加 getTaskStats + Dashboard.vue 改用 stats 端点。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

# 1. workbench.ts 加 getTaskStats
p1 = r"web-ui\apps\admin\src\api\workbench.ts"
c1 = open(p1, encoding="utf-8").read()
if "getTaskStats" not in c1:
    anchor = """  listTasksPage: (
    params?: { status?: CodingTaskStatus; keyword?: string; page?: number; pageSize?: number },
  ) => http.get<WorkbenchTasksPageResponse>('/api/v1/workbench/tasks', params),"""
    new = anchor + """

  /** 任务状态分组计数（仪表盘统计卡，数据库端 GROUP BY）。 */
  getTaskStats: () => http.get<WorkbenchTaskStatsResponse>('/api/v1/workbench/tasks/stats'),"""
    assert anchor in c1, "workbench anchor"
    c1 = c1.replace(anchor, new, 1)
    # 类型导入
    c1 = c1.replace(
        "import type {\n  WorkbenchTaskResponse,\n  WorkbenchTasksPageResponse,",
        "import type {\n  WorkbenchTaskResponse,\n  WorkbenchTasksPageResponse,\n  WorkbenchTaskStatsResponse,",
        1,
    )
    open(p1, "w", encoding="utf-8").write(c1)
    print("workbench.ts: getTaskStats added")

# 2. types-cdss.ts 加类型
p2 = r"web-ui\apps\admin\src\api\types-cdss.ts"
c2 = open(p2, encoding="utf-8").read()
if "WorkbenchTaskStatsResponse" not in c2:
    anchor2 = """/** 工作台任务服务端分页响应（WP-B 前端生产化）。 */
export interface WorkbenchTasksPageResponse {"""
    new2 = """/** 任务状态分组计数（仪表盘统计卡）。 */
export interface WorkbenchTaskStatsResponse {
  total: number
  byStatus: { status: string; count: number }[]
}

/** 工作台任务服务端分页响应（WP-B 前端生产化）。 */
export interface WorkbenchTasksPageResponse {"""
    assert anchor2 in c2, "types anchor"
    c2 = c2.replace(anchor2, new2, 1)
    open(p2, "w", encoding="utf-8").write(c2)
    print("types-cdss.ts: WorkbenchTaskStatsResponse added")

# 3. Dashboard.vue 改用 stats
p3 = r"web-ui\apps\admin\src\views\Dashboard.vue"
c3 = open(p3, encoding="utf-8").read()
old_load = """    const [health, taskList] = await Promise.allSettled([
      healthApi.readiness(),
      canReadCoding.value ? workbenchApi.listTasks() : Promise.resolve<WorkbenchTaskResponse[]>([]),
    ])
    if (health.status === 'fulfilled') {
      healthStatus.value = health.value.status
      serviceChecks.value = health.value.dependencies || {}
    }
    if (taskList.status === 'fulfilled') {
      tasks.value = taskList.value
    }"""
new_load = """    const [health, stats] = await Promise.allSettled([
      healthApi.readiness(),
      canReadCoding.value ? workbenchApi.getTaskStats() : Promise.resolve<WorkbenchTaskStatsResponse | null>(null),
    ])
    if (health.status === 'fulfilled') {
      healthStatus.value = health.value.status
      serviceChecks.value = health.value.dependencies || {}
    }
    if (stats.status === 'fulfilled' && stats.value) {
      // 状态分组计数 → 状态分布（数据库端 GROUP BY，无全量拉取）
      const byStatus: Record<string, number> = {}
      for (const item of stats.value.byStatus) byStatus[item.status] = item.count
      statusCounts.value = byStatus
    }"""
assert old_load in c3, "dashboard load anchor"
c3 = c3.replace(old_load, new_load, 1)

# 4. 导入类型与清理未用导入
c3 = c3.replace(
    "import { ApiError, authApi, healthApi, knowledgeApi, workbenchApi } from '@/api'",
    "import { ApiError, authApi, healthApi, knowledgeApi, workbenchApi } from '@/api'",
)
if "WorkbenchTaskStatsResponse" not in c3.split("from './workbench'") or True:
    pass

open(p3, "w", encoding="utf-8").write(c3)
print("Dashboard.vue switched to stats endpoint")
