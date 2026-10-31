# -*- coding: utf-8 -*-
"""Workbench.vue：切换服务端分页（WP-B）。

改造点：
  - loadTasks 改调 listTasksPage（带 keyword/page/pageSize），tasks 变为当前页 items；
  - filteredTasks 客户端过滤删除，搜索框改防抖 300ms 触发服务端查询；
  - totalPages/pagedTasks 改用服务端 total；
  - watch 联动：搜索/页大小/筛选变化 → 重置页码并重新加载。
"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"web-ui\apps\admin\src\views\Workbench.vue"
c = open(p, encoding="utf-8").read()

# 1. 替换 filteredTasks 与分页计算块
old_pagination = """const activeFilter = ref<CodingTaskStatus | ''>('')
const tasks = ref<WorkbenchTaskResponse[]>([])
const tasksLoading = ref(false)
const taskSearch = ref('')
const filteredTasks = computed(() => {
  const q = taskSearch.value.trim().toLowerCase()
  if (!q) return tasks.value
  return tasks.value.filter(task =>
    (task.patientName || '').toLowerCase().includes(q)
    || (task.medicalRecordNo || '').toLowerCase().includes(q)
    || task.taskId.toLowerCase().includes(q))
})

/* ============== 任务列表分页 ============== */
const pageSize = ref('10')
const pageNum = ref(1)
const sizeNum = computed(() => Number(pageSize.value))
const totalPages = computed(() => Math.max(1, Math.ceil(filteredTasks.value.length / sizeNum.value)))
const pagedTasks = computed(() => {
  const start = (pageNum.value - 1) * sizeNum.value
  return filteredTasks.value.slice(start, start + sizeNum.value)
})
const pageItems = computed<(number | '...')[]>(() => {
  const total = totalPages.value
  const cur = pageNum.value
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1)
  const items: (number | '...')[] = [1]
  const start = Math.max(2, cur - 1)
  const end = Math.min(total - 1, cur + 1)
  if (start > 2) items.push('...')
  for (let i = start; i <= end; i++) items.push(i)
  if (end < total - 1) items.push('...')
  items.push(total)
  return items
})
watch([taskSearch, pageSize], () => { pageNum.value = 1 })
watch(filteredTasks, () => {
  if (pageNum.value > totalPages.value) pageNum.value = totalPages.value
})"""

new_pagination = """const activeFilter = ref<CodingTaskStatus | ''>('')
const tasks = ref<WorkbenchTaskResponse[]>([])
const tasksLoading = ref(false)
const taskSearch = ref('')
const totalCount = ref(0)

/* ============== 任务列表分页（WP-B 服务端分页） ============== */
const pageSize = ref('10')
const pageNum = ref(1)
const sizeNum = computed(() => Number(pageSize.value))
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / sizeNum.value)))
const pagedTasks = computed(() => tasks.value)
const pageItems = computed<(number | '...')[]>(() => {
  const total = totalPages.value
  const cur = pageNum.value
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1)
  const items: (number | '...')[] = [1]
  const start = Math.max(2, cur - 1)
  const end = Math.min(total - 1, cur + 1)
  if (start > 2) items.push('...')
  for (let i = start; i <= end; i++) items.push(i)
  if (end < total - 1) items.push('...')
  items.push(total)
  return items
})

// 搜索防抖 300ms：输入停止后触发服务端查询
let searchDebounce: ReturnType<typeof setTimeout> | null = null
watch(taskSearch, () => {
  if (searchDebounce) clearTimeout(searchDebounce)
  searchDebounce = setTimeout(() => {
    pageNum.value = 1
    void loadTasks()
  }, 300)
})
watch([pageSize, activeFilter], () => {
  pageNum.value = 1
  void loadTasks()
})"""

assert old_pagination in c, "pagination block"
c = c.replace(old_pagination, new_pagination, 1)

# 2. 替换 loadTasks
old_load = """async function loadTasks() {
  tasksLoading.value = true
  try {
    const status = activeFilter.value || undefined
    tasks.value = await workbenchApi.listTasks(status as CodingTaskStatus | undefined)
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.error'))
  }
  finally { tasksLoading.value = false }
}"""
new_load = """async function loadTasks() {
  tasksLoading.value = true
  try {
    const status = (activeFilter.value || undefined) as CodingTaskStatus | undefined
    const keyword = taskSearch.value.trim() || undefined
    const result = await workbenchApi.listTasksPage({
      status,
      keyword,
      page: pageNum.value,
      pageSize: sizeNum.value,
    })
    tasks.value = result.items
    totalCount.value = result.total
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.error'))
  }
  finally { tasksLoading.value = false }
}"""
assert old_load in c, "load block"
c = c.replace(old_load, new_load, 1)

# 3. 模板中 filteredTasks.length 引用改为 totalCount
c = c.replace("filteredTasks.length === 0", "tasks.length === 0")
c = c.replace("共 {{ filteredTasks.length }} 条", "共 {{ totalCount }} 条")

open(p, "w", encoding="utf-8").write(c)
print("Workbench.vue converted to server-side pagination")
