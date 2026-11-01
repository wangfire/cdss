# -*- coding: utf-8 -*-
"""Dashboard.vue 混合方案：
   - stats 端点 → statusCounts（全量计数，统计卡总数正确）
   - listTasksPage({pageSize:100}) → tasks（前 100 条明细：推荐数合计/近期趋势/列表展示）
"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"web-ui\apps\admin\src\views\Dashboard.vue"
c = open(p, encoding="utf-8").read()

old = """    const [health, stats] = await Promise.allSettled([
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

new = """    const [health, stats, recentPage] = await Promise.allSettled([
      healthApi.readiness(),
      canReadCoding.value ? workbenchApi.getTaskStats() : Promise.resolve<WorkbenchTaskStatsResponse | null>(null),
      canReadCoding.value
        ? workbenchApi.listTasksPage({ page: 1, pageSize: 100 })
        : Promise.resolve<WorkbenchTasksPageResponse | null>(null),
    ])
    if (health.status === 'fulfilled') {
      healthStatus.value = health.value.status
      serviceChecks.value = health.value.dependencies || {}
    }
    if (stats.status === 'fulfilled' && stats.value) {
      // 状态分组计数（数据库端 GROUP BY，全量口径）
      const byStatus: Record<string, number> = {}
      for (const item of stats.value.byStatus) byStatus[item.status] = item.count
      statusCounts.value = byStatus
    }
    if (recentPage.status === 'fulfilled' && recentPage.value) {
      // 最近 100 条明细（推荐数合计/近期趋势/列表展示）
      tasks.value = recentPage.value.items
    }"""

assert old in c, "hybrid anchor"
c = c.replace(old, new, 1)

# statusCounts 从 computed 改为 ref（现在是服务端数据，不再从 tasks 派生）
old_sc = """const statusCounts = computed(() => {
  const counts: Record<string, number> = {}
  for (const task of tasks.value) {
    counts[task.status] = (counts[task.status] || 0) + 1
  }
  return counts
})"""
new_sc = """// 状态分布：服务端 stats 端点全量计数（非客户端从 tasks 派生）
const statusCounts = ref<Record<string, number>>({})"""
if old_sc in c:
    c = c.replace(old_sc, new_sc, 1)
    print("statusCounts computed -> ref")

# 导入类型
if "WorkbenchTasksPageResponse" not in c:
    c = c.replace(
        "import type { WorkbenchTaskResponse } from '@/api'",
        "import type { WorkbenchTaskResponse, WorkbenchTaskStatsResponse, WorkbenchTasksPageResponse } from '@/api'",
        1,
    )
    print("types imported")

# 确认 statusCounts 在 loadData 赋值前已声明（ref 声明位置须在 loadData 之前——检查顺序）
idx_sc = c.find("const statusCounts = ref")
idx_load = c.find("async function loadData")
print(f"order ok: {idx_sc < idx_load} (sc@{idx_sc}, load@{idx_load})")

open(p, "w", encoding="utf-8").write(c)
