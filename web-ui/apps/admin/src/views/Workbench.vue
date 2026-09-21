<script setup lang="ts">
import {
  ArrowLeft,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  ClipboardList,
  History,
  Loader2,
  Search,
  ShieldCheck,
  Sparkles,
  XCircle,
} from 'lucide-vue-next'
import { computed, nextTick, onActivated, onBeforeUnmount, onMounted, onUpdated, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { ApiError, workbenchApi } from '@/api'
import type {
  CodingRecommendationResponse,
  CodingTaskStatus,
  PipelineRunResponse,
  ReviewedCodeItem,
  WorkbenchTaskResponse,
} from '@/api'
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  Input,
  Label,
  ScrollArea,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Separator,
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  toast,
} from '@tabtab/ui'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

/* ============== 状态筛选 ============== */
const statusFilters: { key: string; labelKey: string; value: CodingTaskStatus | '' }[] = [
  { key: 'all', labelKey: 'workbench.filterAll', value: '' },
  { key: 'pending', labelKey: 'workbench.filterPending', value: 'PENDING' },
  { key: 'running', labelKey: 'workbench.filterRunning', value: 'RUNNING' },
  { key: 'human', labelKey: 'workbench.filterHumanRequired', value: 'HUMAN_REQUIRED' },
  { key: 'review', labelKey: 'workbench.filterPendingReview', value: 'PENDING_REVIEW' },
  { key: 'accepted', labelKey: 'workbench.filterAccepted', value: 'ACCEPTED' },
  { key: 'modified', labelKey: 'workbench.filterModified', value: 'MODIFIED' },
  { key: 'rejected', labelKey: 'workbench.filterRejected', value: 'REJECTED' },
  { key: 'failed', labelKey: 'workbench.filterFailed', value: 'FAILED' },
]

const activeFilter = ref<CodingTaskStatus | ''>('')
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
})

/* ============== 任务详情 ============== */
const selectedTaskId = ref<string | null>(null)
const recommendations = ref<CodingRecommendationResponse[]>([])
const recsLoading = ref(false)
const reviewComment = ref('')
const reviewSubmitting = ref(false)
const selectedCodes = ref<Set<string>>(new Set())

const selectedTask = computed(() => tasks.value.find(t => t.taskId === selectedTaskId.value))

/* ============== 推荐分类 ============== */
// 历史数据中部分手术推荐的 recommendation_type 被错标为 DIAGNOSIS，
// 以编码体系为准判断：ICD-9-CM-3 是手术分类，ICD-10 是诊断分类
function isProcedureRec(rec: CodingRecommendationResponse) {
  return rec.recommendationType === 'PROCEDURE' || rec.codeSystem.toUpperCase().includes('ICD-9')
}
/* 入院诊断：文书自动抽取的诊断输入（入院独有条目），出院诊断为首页结构化输入 FRONT_PAGE。 */
function isAdmissionRec(rec: CodingRecommendationResponse) {
  return !isProcedureRec(rec)
    && (rec.diagnosisSourceType === 'DOCUMENT_AUTO' || rec.diagnosisSourceType === 'ADMISSION_PAGE')
}
const admissionRecommendations = computed(() =>
  recommendations.value.filter(isAdmissionRec),
)
const diagnosisRecommendations = computed(() =>
  recommendations.value.filter(rec => !isProcedureRec(rec) && !isAdmissionRec(rec)),
)
const procedureRecommendations = computed(() =>
  recommendations.value.filter(isProcedureRec),
)
const activeRecTab = ref('admission')
const activeRecommendations = computed(() =>
  activeRecTab.value === 'admission'
    ? admissionRecommendations.value
    : activeRecTab.value === 'diagnosis'
        ? diagnosisRecommendations.value
        : procedureRecommendations.value,
)

/* 按医生输入分组：同一输入（名称+主/次类型）的候选码归为一组，组内按置信度降序，便于逐组对比审核。
   组间顺序：主诊断组排第一，其余按诊断输入的导入顺序（diagnosisOrder）排列。 */
const recommendationGroups = computed(() => {
  const groups: { key: string, name: string, code: string, typeLabel: string, recs: CodingRecommendationResponse[], principal: boolean, order: number }[] = []
  const byKey = new Map<string, (typeof groups)[number]>()
  for (const rec of activeRecommendations.value) {
    const parts = doctorParts(rec)
    const typeLabel = diagnosisTypeLabel(rec)
    const key = `${parts.name}|${typeLabel}`
    let group = byKey.get(key)
    if (!group) {
      group = { key, name: parts.name || rec.title, code: parts.code, typeLabel, recs: [], principal: false, order: Number.MAX_SAFE_INTEGER }
      byKey.set(key, group)
      groups.push(group)
    }
    group.recs.push(rec)
    if (rec.isPrincipal === true) group.principal = true
    if (typeof rec.diagnosisOrder === 'number' && rec.diagnosisOrder < group.order)
      group.order = rec.diagnosisOrder
  }
  for (const group of groups) {
    group.recs.sort((a, b) => b.confidenceScore - a.confidenceScore)
  }
  groups.sort((a, b) => (Number(b.principal) - Number(a.principal)) || (a.order - b.order))
  return groups
})

/* ============== 医生诊断展示 ============== */
const doctorDiagnosisCache = new Map<string, { code: string, name: string }>()
function doctorParts(rec: CodingRecommendationResponse) {
  const cached = doctorDiagnosisCache.get(rec.id)
  if (cached) return cached
  const raw = (rec.doctorDiagnosisText || '').trim()
  // 医生诊断编码可能出现在文本前缀或后缀，如 "E11.9 2型糖尿病" / "原发性高血压I10x00"
  let m = raw.match(/^([A-Z]\d{2}(?:[.xX]\w{1,4})?)\s+(.+)$/)
  if (!m) m = raw.match(/^(.+?)\s*([A-Z]\d{2}(?:[.xX]\w{1,4})?)$/) || null
  const parts = m && m[2]!.trim()
    ? (m[1]!.startsWith(raw.slice(0, m[1]!.length)) && /^[A-Z]\d/.test(m[1]!)
        ? { code: m[1]!, name: m[2]!.trim() }
        : { code: m[2]!, name: m[1]!.trim() })
    : { code: '', name: raw || rec.title }
  doctorDiagnosisCache.set(rec.id, parts)
  return parts
}
function diagnosisTypeLabel(rec: CodingRecommendationResponse) {
  if (isProcedureRec(rec)) return '手术'
  if (isAdmissionRec(rec)) return '入院诊断'
  if (rec.isPrincipal === true) return '主诊断'
  if (rec.isPrincipal === false) return '次诊断'
  return '诊断'
}

/* ============== 加载任务列表 ============== */
async function loadTasks() {
  tasksLoading.value = true
  try {
    const status = activeFilter.value || undefined
    tasks.value = await workbenchApi.listTasks(status as CodingTaskStatus | undefined)
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.error'))
  }
  finally { tasksLoading.value = false }
}

/* ============== 加载推荐 ============== */
async function loadRecommendations(taskId: string) {
  selectedTaskId.value = taskId
  recsLoading.value = true
  selectedCodes.value = new Set()
  reviewComment.value = ''
  try {
    const result = await workbenchApi.getRecommendations(taskId)
    recommendations.value = result.recommendations
    // 默认全选
    for (const rec of result.recommendations) {
      selectedCodes.value.add(rec.id)
    }
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.error'))
    recommendations.value = []
  }
  finally { recsLoading.value = false }
}

/* ============== 流水线运行记录（Trace 审计） ============== */
const tracesOpen = ref(false)
const tracesLoading = ref(false)
const traces = ref<PipelineRunResponse[]>([])
const expandedRun = ref<string | null>(null)

const stageLabels: Record<string, string> = {
  QUALITY_GATE: '数据质量门',
  DOCUMENT_VERSION: '文书版本',
  CHUNK: '文本分段',
  FACT: '事实抽取',
  EVIDENCE: '证据生成',
  EXACT_RETRIEVAL: '精确检索',
  BM25_RETRIEVAL: '词法检索(BM25)',
  RULE: '规则引擎',
  SCORE: '评分',
  POLICY: '策略判定',
  PERSIST: '结果落库',
}

const degradedLabels: Record<string, string> = {
  'DEGRADED.NO_BM25': '检索引擎不可用',
  'DEGRADED.NO_VECTOR': '向量检索未接入',
  'DEGRADED.NO_MODEL': '大模型未接入',
  'DEGRADED.LLM_OUTPUT_INVALID': '模型输出未通过校验',
}

function stageLabel(stage: string) {
  return stageLabels[stage] || stage
}

function degradedLabel(flag: string) {
  return degradedLabels[flag] || flag
}

function runStatusClass(status: string) {
  if (status === 'SUCCESS') return 'text-emerald-600 dark:text-emerald-400'
  if (status === 'FAILED') return 'text-red-600 dark:text-red-400'
  if (status === 'DEGRADED') return 'text-amber-600 dark:text-amber-400'
  return 'text-orange-600 dark:text-orange-400'
}

function formatTime(iso: string) {
  return new Date(iso).toLocaleString('zh-CN', { hour12: false })
}

async function openTraces() {
  if (!selectedTaskId.value) return
  tracesOpen.value = true
  tracesLoading.value = true
  try {
    traces.value = await workbenchApi.getTraces(selectedTaskId.value)
    expandedRun.value = traces.value[0]?.runId || traces.value[0]?.traceId || null
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.error'))
    traces.value = []
  }
  finally { tracesLoading.value = false }
}

function toggleRun(run: PipelineRunResponse) {
  const key = run.runId || run.traceId
  expandedRun.value = expandedRun.value === key ? null : key
}

/* ============== 提交审核 ============== */
async function submitReview(reviewStatus: string) {
  if (!selectedTaskId.value || reviewSubmitting.value) return
  reviewSubmitting.value = true
  try {
    const finalCodes: ReviewedCodeItem[] = []
    if (reviewStatus === 'ACCEPTED' || reviewStatus === 'MODIFIED') {
      for (const rec of recommendations.value) {
        if (selectedCodes.value.has(rec.id)) {
          finalCodes.push({
            recommendationId: rec.id,
            resultType: isProcedureRec(rec) ? 'PROCEDURE' : 'DIAGNOSIS',
            codeSystem: rec.codeSystem,
            code: rec.code,
            title: rec.title,
          })
        }
      }
    }
    await workbenchApi.submitReview(selectedTaskId.value, {
      reviewStatus,
      finalCodes,
      comment: reviewComment.value || null,
    })
    toast.success(t('workbench.reviewSuccess'))
    await loadTasks()
    if (selectedTaskId.value) await loadRecommendations(selectedTaskId.value)
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('workbench.reviewFailed'))
  }
  finally { reviewSubmitting.value = false }
}

function toggleCode(id: string) {
  const set = new Set(selectedCodes.value)
  if (set.has(id)) set.delete(id)
  else set.add(id)
  selectedCodes.value = set
}

/* ============== 高度自适应：面板底边距滚动容器可视底部 10px，不出页面滚动条 ============== */
const gridEl = ref<HTMLElement | null>(null)
const panelH = ref(0)
const panelStyle = computed(() => ({
  '--wb-h': panelH.value > 0 ? `${panelH.value}px` : 'calc(100vh - 136px)',
}))
let scrollHost: HTMLElement | null = null
let hostObserver: ResizeObserver | null = null
function measureGrid() {
  const grid = gridEl.value
  if (!grid) return
  let host: HTMLElement | null = grid.parentElement
  while (host && !['auto', 'scroll'].includes(getComputedStyle(host).overflowY)) {
    host = host.parentElement
  }
  // 滚动宿主（LayoutContent 的 ScrollArea 视口）高度会随页签栏出现/换行变化，需持续监听重算
  if (host !== scrollHost) {
    scrollHost = host
    hostObserver?.disconnect()
    hostObserver = null
  }
  if (host && !hostObserver) {
    hostObserver = new ResizeObserver(() => measureGrid())
    hostObserver.observe(host)
  }
  const isDoc = host === null
  const hostEl = (host ?? document.documentElement) as HTMLElement
  const hostRect = hostEl.getBoundingClientRect()
  const gridRect = grid.getBoundingClientRect()
  const scrollTop = isDoc ? window.scrollY : hostEl.scrollTop
  const padT = parseFloat(getComputedStyle(hostEl).paddingTop) || 0
  // 网格底边到滚动内容底部之间的附加内容高度（如 LayoutPage 的 padding-bottom）
  let belowPad = 0
  let anc2: HTMLElement | null = grid.parentElement
  while (anc2 && anc2 !== hostEl) {
    belowPad += parseFloat(getComputedStyle(anc2).paddingBottom) || 0
    anc2 = anc2.parentElement
  }
  if (isDoc) belowPad = 0
  // 负 margin 抵消祖先 padding-bottom，使面板底边距视口底部恰好 10px 且不出现滚动条
  grid.style.marginBottom = belowPad ? `-${belowPad}px` : ''
  // 网格顶边相对滚动内容区顶部（含宿主 padding-top，不受当前滚动影响）
  const gridTopInContent = gridRect.top - hostRect.top + scrollTop + padT
  const visibleH = isDoc ? window.innerHeight : hostRect.height
  panelH.value = Math.max(300, Math.round(visibleH - gridTopInContent - 10))
}
onMounted(() => {
  measureGrid()
  nextTick(() => { measureGrid(); setupFilterScroll() })
  window.addEventListener('resize', measureGrid)
})
// 模板补丁式重渲染可能替换 TabsList DOM，需重新绑定滚动监听
onUpdated(setupFilterScroll)
onBeforeUnmount(() => {
  window.removeEventListener('resize', measureGrid)
  hostObserver?.disconnect()
  hostObserver = null
  scrollHost = null
  filterListEl?.removeEventListener('scroll', updateFilterArrows)
  filterListObserver?.disconnect()
  filterListObserver = null
  filterListEl = null
})

/* ============== 状态筛选：单行 + 左右箭头（位置固定占位，单击跳到两端） ============== */
const tabsListRef = ref<{ $el?: HTMLElement } | HTMLElement | null>(null)
const canScrollL = ref(false)
const canScrollR = ref(false)
let filterListEl: HTMLElement | null = null
let filterListObserver: ResizeObserver | null = null
function updateFilterArrows() {
  if (!filterListEl || !filterListEl.isConnected) return
  canScrollL.value = filterListEl.scrollLeft > 2
  canScrollR.value = filterListEl.scrollLeft + filterListEl.clientWidth < filterListEl.scrollWidth - 2
}
function setupFilterScroll() {
  const r = tabsListRef.value as { $el?: HTMLElement } | HTMLElement | null
  const el = (r && ('$el' in r ? r.$el : r)) ?? null
  if (el === filterListEl && el?.isConnected) { updateFilterArrows(); return }
  filterListEl?.removeEventListener('scroll', updateFilterArrows)
  filterListEl = el instanceof HTMLElement ? el : null
  if (!filterListEl) return
  filterListEl.scrollLeft = 0
  filterListEl.addEventListener('scroll', updateFilterArrows, { passive: true })
  filterListObserver?.disconnect()
  filterListObserver = new ResizeObserver(updateFilterArrows)
  filterListObserver.observe(filterListEl)
  updateFilterArrows()
}
function scrollFilter(dir: number) {
  if (!filterListEl) return
  // 不用 smooth：滚动过程中宽度变化会打断动画导致一次点击到不了底
  filterListEl.scrollLeft = dir > 0 ? filterListEl.scrollWidth : 0
}

/* ============== 状态路由参数 ============== */
watch(() => route.query.task, (taskId) => {
  if (typeof taskId === 'string' && taskId) loadRecommendations(taskId)
}, { immediate: true })

onMounted(() => {
  loadTasks()
})

onActivated(() => {
  loadTasks()
  nextTick(measureGrid)
})

const statusLabel = (status: CodingTaskStatus) => {
  const map: Record<string, string> = {
    PENDING: t('workbench.statusPending'), RUNNING: t('workbench.statusRunning'),
    SUCCESS: t('workbench.statusSuccess'), FAILED: t('workbench.statusFailed'),
    RETRYING: t('workbench.statusRetrying'), TIMEOUT: t('workbench.statusTimeout'),
    CANCELLED: t('workbench.statusCancelled'), HUMAN_REQUIRED: t('workbench.statusHumanRequired'),
    PENDING_REVIEW: t('workbench.statusPendingReview'), ACCEPTED: t('workbench.statusAccepted'),
    MODIFIED: t('workbench.statusModified'), REJECTED: t('workbench.statusRejected'),
  }
  return map[status] || status
}

const statusColor = (status: CodingTaskStatus) => {
  const map: Record<string, string> = {
    PENDING: 'bg-slate-500/10 text-slate-600 border-slate-200',
    RUNNING: 'bg-blue-500/10 text-blue-600 border-blue-200',
    SUCCESS: 'bg-emerald-500/10 text-emerald-600 border-emerald-200',
    FAILED: 'bg-red-500/10 text-red-600 border-red-200',
    RETRYING: 'bg-amber-500/10 text-amber-600 border-amber-200',
    TIMEOUT: 'bg-orange-500/10 text-orange-600 border-orange-200',
    CANCELLED: 'bg-slate-500/10 text-slate-600 border-slate-200',
    HUMAN_REQUIRED: 'bg-violet-500/10 text-violet-600 border-violet-200',
    PENDING_REVIEW: 'bg-violet-500/10 text-violet-600 border-violet-200',
    ACCEPTED: 'bg-emerald-500/10 text-emerald-600 border-emerald-200',
    MODIFIED: 'bg-blue-500/10 text-blue-600 border-blue-200',
    REJECTED: 'bg-red-500/10 text-red-600 border-red-200',
  }
  return map[status] || 'bg-slate-500/10 text-slate-600 border-slate-200'
}

const canReview = computed(() => {
  const s = selectedTask.value?.status
  return s === 'HUMAN_REQUIRED' || s === 'PENDING_REVIEW'
})
</script>

<template>
  <div class="space-y-6">
    <div ref="gridEl" class="grid grid-cols-1 lg:grid-cols-10 gap-6" :style="panelStyle">
      <!-- 左侧：任务列表 -->
      <div class="lg:col-span-3 flex flex-col gap-4" style="height: var(--wb-h);">
        <!-- 状态筛选：单行 + 悬浮左右箭头，箭头不占宽度，显示时内容侧自动加 padding 避让 -->
        <div class="relative">
          <Tabs v-model="activeFilter" @update:model-value="loadTasks">
            <TabsList
              ref="tabsListRef"
              class="w-full h-auto gap-1 bg-transparent flex-nowrap justify-start overflow-x-auto no-scrollbar transition-[padding]"
              :class="{ 'pl-7': canScrollL, 'pr-7': canScrollR }"
            >
              <TabsTrigger
                v-for="f in statusFilters"
                :key="f.key"
                :value="f.value"
                class="shrink-0 whitespace-nowrap text-[11px] px-2 py-0.5 leading-none rounded-full border border-border/60 data-[state=active]:border-primary/40"
              >
                {{ t(f.labelKey as any) }}
              </TabsTrigger>
            </TabsList>
          </Tabs>
          <button
            v-show="canScrollL"
            class="absolute left-0 top-1/2 z-10 flex h-6 w-6 -translate-y-1/2 items-center justify-center rounded-full border bg-background shadow"
            @click="scrollFilter(-1)"
          >
            <ChevronLeft class="h-3.5 w-3.5" />
          </button>
          <button
            v-show="canScrollR"
            class="absolute right-0 top-1/2 z-10 flex h-6 w-6 -translate-y-1/2 items-center justify-center rounded-full border bg-background shadow"
            @click="scrollFilter(1)"
          >
            <ChevronRight class="h-3.5 w-3.5" />
          </button>
        </div>

        <!-- 查询搜索：姓名 / 病案号 -->
        <div class="relative">
          <Search class="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input
            v-model="taskSearch"
            placeholder="按姓名、病案号查询..."
            class="h-8 pl-8 pr-8 text-xs"
          />
          <button
            v-if="taskSearch"
            class="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
            @click="taskSearch = ''"
          >
            <XCircle class="h-3.5 w-3.5" />
          </button>
        </div>

        <!-- 任务列表 -->
        <Card class="border border-border/40 flex-1 min-h-0 flex flex-col py-0 gap-0">
          <CardContent class="p-0 flex-1 min-h-0">
            <ScrollArea class="h-full">
              <div v-if="tasksLoading" class="flex items-center justify-center py-12">
                <Loader2 class="h-6 w-6 animate-spin text-muted-foreground" />
              </div>
              <div v-else-if="filteredTasks.length === 0" class="text-center py-12 text-muted-foreground">
                <ClipboardList class="h-10 w-10 mx-auto mb-3 opacity-40" />
                <p class="text-sm">{{ t('workbench.noTasks') }}</p>
                <p class="text-xs mt-1">{{ t('workbench.noTasksDesc') }}</p>
              </div>
              <div v-else class="divide-y divide-border/50">
                <div
                  v-for="task in pagedTasks"
                  :key="task.taskId"
                  class="px-3 py-2 hover:bg-muted/50 cursor-pointer transition-colors"
                  :class="{ 'bg-violet-500/5 border-l-2 border-l-violet-500': selectedTaskId === task.taskId }"
                  @click="loadRecommendations(task.taskId)"
                >
                  <div class="flex items-center justify-between gap-2">
                    <span class="text-sm font-semibold text-foreground truncate">
                      {{ task.patientName || '未知患者' }}
                    </span>
                    <Badge variant="outline" class="text-xs shrink-0" :class="statusColor(task.status)">
                      {{ statusLabel(task.status) }}
                    </Badge>
                  </div>
                  <div class="flex items-center justify-between gap-2 text-xs text-muted-foreground mt-0.5">
                    <span class="truncate">
                      病案号: {{ task.medicalRecordNo || '-' }} · 住院 {{ task.admissionCount }} 次 · {{ t('workbench.recommendations') }}: {{ task.recommendationCount }}
                    </span>
                    <span class="shrink-0">
                      {{ new Date(task.dischargeAt || task.createdAt).toLocaleDateString('zh-CN') }}
                    </span>
                  </div>
                </div>
              </div>
            </ScrollArea>
          </CardContent>
          <!-- 分页：每页条数 + 页码 -->
          <div class="flex items-center justify-between gap-1 px-3 py-1.5 border-t border-border/50 shrink-0">
            <div class="flex items-center gap-1 min-w-0 text-xs text-muted-foreground whitespace-nowrap">
              <Select v-model="pageSize">
                <SelectTrigger class="h-7! w-[64px] shrink-0 text-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="s in ['10', '20', '30', '50', '100']" :key="s" :value="s" class="text-xs">
                    {{ s }} 条
                  </SelectItem>
                </SelectContent>
              </Select>
              <span class="whitespace-nowrap">共 {{ filteredTasks.length }} 条</span>
            </div>
            <div class="flex items-center shrink-0">
              <Button variant="ghost" size="icon" class="h-6 w-6 shrink-0" :disabled="pageNum <= 1" @click="pageNum--">
                <ChevronLeft class="h-3.5 w-3.5" />
              </Button>
              <template v-for="(p, idx) in pageItems" :key="idx">
                <span v-if="p === '...'" class="w-4 shrink-0 text-center text-xs text-muted-foreground">…</span>
                <button
                  v-else
                  class="h-6 w-6 shrink-0 rounded text-xs"
                  :class="p === pageNum ? 'bg-primary text-primary-foreground font-medium' : 'text-muted-foreground hover:bg-muted hover:text-foreground'"
                  @click="pageNum = p"
                >
                  {{ p }}
                </button>
              </template>
              <Button variant="ghost" size="icon" class="h-6 w-6 shrink-0" :disabled="pageNum >= totalPages" @click="pageNum++">
                <ChevronRight class="h-3.5 w-3.5" />
              </Button>
            </div>
          </div>
        </Card>
      </div>

      <!-- 右侧：任务详情 + 推荐 -->
      <div class="lg:col-span-7 flex flex-col gap-4" style="height: var(--wb-h);">
        <template v-if="!selectedTaskId">
          <Card class="border border-border/40 flex-1">
            <CardContent class="flex flex-col items-center justify-center py-20 text-muted-foreground">
              <ClipboardList class="h-12 w-12 mb-4 opacity-30" />
              <p class="text-sm">{{ t('workbench.viewDetails') }}</p>
              <p class="text-xs mt-1">{{ t('workbench.noTasksDesc') }}</p>
            </CardContent>
          </Card>
        </template>

        <template v-else>
          <!-- 推荐列表（可滚动） -->
          <Card class="border border-border/40 flex-1 min-h-0 flex flex-col py-0 gap-0">
            <CardHeader class="pt-2 pb-3 flex-shrink-0">
              <div class="flex items-center justify-between">
                <CardTitle class="flex items-center gap-2 text-base font-semibold">
                  <Sparkles class="h-4 w-4 text-violet-500" />
                  {{ t('workbench.recommendationsTitle') }}
                </CardTitle>
                <div class="flex items-center gap-2">
                  <Badge variant="secondary">{{ recommendations.length }}</Badge>
                  <Button variant="ghost" size="sm" class="h-6 px-2 text-xs" @click="openTraces">
                    <History class="h-3.5 w-3.5 mr-1" />
                    运行记录
                  </Button>
                </div>
              </div>
              <div class="mt-3 flex items-center gap-3">
                <Tabs v-model="activeRecTab" class="flex-1 min-w-0">
                  <TabsList class="w-full">
                    <TabsTrigger value="admission" class="flex-1">
                      入院诊断 ({{ admissionRecommendations.length }})
                    </TabsTrigger>
                    <TabsTrigger value="diagnosis" class="flex-1">
                      诊断 ({{ diagnosisRecommendations.length }})
                    </TabsTrigger>
                    <TabsTrigger value="procedure" class="flex-1">
                      手术 ({{ procedureRecommendations.length }})
                    </TabsTrigger>
                  </TabsList>
                </Tabs>
                <!-- 任务信息（右移、占一半宽度，与诊断/手术页签顶部对齐） -->
                <div class="w-1/2 flex items-center justify-end gap-2 min-w-0">
                  <Button variant="ghost" size="icon" class="h-6 w-6 flex-shrink-0" @click="selectedTaskId = null">
                    <ArrowLeft class="h-3.5 w-3.5" />
                  </Button>
                  <span class="text-sm font-semibold truncate">{{ selectedTask?.patientName || '未知患者' }}</span>
                  <span class="text-xs text-muted-foreground truncate">
                    病案号: {{ selectedTask?.medicalRecordNo || '-' }} · 住院 {{ selectedTask?.admissionCount ?? 0 }} 次
                  </span>
                  <Badge v-if="selectedTask" variant="outline" class="text-xs flex-shrink-0" :class="statusColor(selectedTask.status)">
                    {{ statusLabel(selectedTask.status) }}
                  </Badge>
                </div>
              </div>
            </CardHeader>
            <CardContent class="pt-0 flex-1 min-h-0 overflow-y-auto">
              <div v-if="recsLoading" class="flex items-center justify-center py-12">
                <Loader2 class="h-6 w-6 animate-spin text-muted-foreground" />
              </div>
              <div v-else-if="recommendations.length === 0" class="text-center py-12 text-muted-foreground">
                <Search class="h-10 w-10 mx-auto mb-3 opacity-40" />
                <p class="text-sm">{{ t('workbench.noRecommendations') }}</p>
                <p class="text-xs mt-1">{{ t('workbench.noRecommendationsDesc') }}</p>
              </div>
              <template v-else>
                <div v-if="activeRecommendations.length === 0" class="text-center py-8 text-muted-foreground">
                  <p class="text-sm">{{ activeRecTab === 'admission' ? '暂无入院诊断推荐' : activeRecTab === 'diagnosis' ? '暂无诊断推荐' : '暂无手术推荐' }}</p>
                </div>
                <div v-else class="space-y-2">
                  <div
                    v-for="grp in recommendationGroups"
                    :key="grp.key"
                    class="rounded-md border border-border/60 overflow-visible"
                  >
                    <!-- 组头：医生填写的临床名称 + 类型 + 医生编码 -->
                    <div class="flex items-center gap-2 px-3 py-1.5 bg-muted/50 rounded-t-md">
                      <span class="text-sm font-semibold text-foreground truncate" :title="grp.name">{{ grp.name }}</span>
                      <Badge variant="outline" class="text-[10px] px-1 py-0 shrink-0 font-normal">{{ grp.typeLabel }}</Badge>
                      <span v-if="grp.code" class="text-xs font-mono text-muted-foreground shrink-0">{{ grp.code }}</span>
                      <span class="ml-auto shrink-0 text-[10px] text-muted-foreground">{{ grp.recs.length }} 个推荐</span>
                    </div>
                    <div class="divide-y divide-border/40">
                      <div
                        v-for="rec in grp.recs"
                        :key="rec.id"
                        class="group relative px-3 py-2 hover:bg-muted/30 transition-colors"
                      >
                        <!-- 行：推荐诊断名称 | 选择 + 推荐编码 + 置信度 -->
                        <div class="flex items-center justify-between gap-2">
                          <div class="flex items-center gap-2 min-w-0">
                            <span class="text-sm font-medium text-foreground truncate" :title="rec.title">
                              {{ rec.title }}
                            </span>
                          </div>
                          <div class="flex items-center gap-2 shrink-0">
                            <label v-if="canReview" class="flex items-center gap-1.5 cursor-pointer text-xs">
                              <input
                                type="checkbox"
                                :checked="selectedCodes.has(rec.id)"
                                @change="toggleCode(rec.id)"
                                class="rounded border-border"
                              />
                              {{ t('workbench.selectCodes') }}
                            </label>
                            <Badge variant="secondary" class="text-xs">{{ rec.codeSystem }}</Badge>
                            <span class="text-sm font-mono text-primary">{{ rec.code }}</span>
                            <span class="text-xs text-muted-foreground">
                              {{ t('workbench.confidence') }} <strong class="text-foreground">{{ (rec.confidenceScore * 100).toFixed(1) }}%</strong>
                            </span>
                          </div>
                        </div>
                        <!-- 证据链：鼠标悬停显示（完整推荐名称 + 证据），不占列表高度 -->
                        <div
                          class="hidden group-hover:block absolute left-0 right-0 top-full z-20 mt-1 rounded-md border border-border/60 bg-popover p-2 shadow-lg"
                        >
                          <template v-if="rec.evidences.length > 0">
                            <p class="text-xs font-medium text-muted-foreground mb-1">
                              {{ t('workbench.evidence') }} ({{ rec.evidences.length }})
                            </p>
                            <div class="space-y-1">
                              <div v-for="(ev, idx) in rec.evidences.slice(0, 6)" :key="idx" class="text-xs bg-muted/50 rounded px-2 py-1">
                                <div class="flex items-center gap-2">
                                  <Badge variant="outline" class="text-[10px] px-1 py-0">{{ ev.sourceType }}</Badge>
                                  <span class="text-muted-foreground">{{ t('workbench.score') }}: {{ ev.score.toFixed(3) }}</span>
                                </div>
                                <p class="text-foreground/80" :title="ev.sourceText">{{ ev.sourceText }}</p>
                                <p v-if="ev.matchText && ev.matchText !== ev.sourceText" class="text-primary/80" :title="ev.matchText">→ {{ ev.matchText }}</p>
                              </div>
                              <p v-if="rec.evidences.length > 6" class="text-[10px] text-muted-foreground">
                                仅显示前 6 条，共 {{ rec.evidences.length }} 条
                              </p>
                            </div>
                          </template>
                          <p v-else class="text-xs text-muted-foreground">无证据链</p>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </template>
            </CardContent>
          </Card>

          <!-- 审核操作 -->
          <Card v-if="canReview" class="border border-border/40 flex-shrink-0 py-2 gap-[5px]">
            <div class="flex items-center gap-2 px-4">
              <ShieldCheck class="h-4 w-4 text-violet-500" />
              <span class="text-sm font-semibold">{{ t('workbench.review') }}</span>
            </div>
            <div class="px-4 space-y-2">
              <Textarea
                v-model="reviewComment"
                :placeholder="t('workbench.reviewCommentPlaceholder')"
                rows="2"
                class="text-sm"
              />
              <div class="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="default"
                  :disabled="reviewSubmitting || selectedCodes.size === 0"
                  @click="submitReview('ACCEPTED')"
                >
                  <CheckCircle2 v-if="!reviewSubmitting" class="h-3.5 w-3.5 mr-1" />
                  <Loader2 v-else class="h-3.5 w-3.5 mr-1 animate-spin" />
                  {{ t('workbench.acceptAll') }}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  :disabled="reviewSubmitting"
                  @click="submitReview('MODIFIED')"
                >
                  {{ t('workbench.submitReview') }}
                </Button>
                <Button
                  size="sm"
                  variant="destructive"
                  :disabled="reviewSubmitting"
                  @click="submitReview('REJECTED')"
                >
                  <XCircle class="h-3.5 w-3.5 mr-1" />
                  {{ t('workbench.rejectAll') }}
                </Button>
              </div>
            </div>
          </Card>
        </template>
      </div>
    </div>
  </div>

  <!-- 流水线运行记录（Trace 审计）抽屉 -->
  <Sheet v-model:open="tracesOpen">
    <SheetContent side="right" class="w-[480px] sm:max-w-[480px] p-0 gap-0 flex flex-col">
      <SheetHeader class="px-4 py-3 border-b border-border/40">
        <SheetTitle class="flex items-center gap-2 text-base">
          <History class="h-4 w-4 text-violet-500" />
          流水线运行记录
        </SheetTitle>
        <SheetDescription class="text-xs">
          {{ selectedTask?.patientName || '当前病例' }} · 每次推荐生成的步骤、降级标记与质量问题（最近 20 次）
        </SheetDescription>
      </SheetHeader>
      <ScrollArea class="flex-1 min-h-0">
        <div class="p-3 space-y-2">
          <div v-if="tracesLoading" class="flex items-center justify-center py-8 text-sm text-muted-foreground">
            <Loader2 class="h-4 w-4 animate-spin mr-2" />加载中...
          </div>
          <p v-else-if="traces.length === 0" class="py-8 text-center text-sm text-muted-foreground">
            暂无运行记录
          </p>
          <template v-else>
            <div
              v-for="run in traces"
              :key="run.runId || run.traceId"
              class="rounded-md border border-border/50"
            >
            <button
              class="w-full flex items-center gap-2 px-3 py-2 text-left hover:bg-muted/50"
              @click="toggleRun(run)"
            >
              <span class="text-xs w-36 flex-shrink-0">{{ formatTime(run.startedAt) }}</span>
              <span class="text-xs font-medium w-20 flex-shrink-0" :class="runStatusClass(run.status)">
                {{ run.status }}
              </span>
              <span class="text-xs text-muted-foreground">
                推荐 {{ run.recommendationCount }} 条 · {{ (run.durationMs ?? 0) }}ms
              </span>
              <Badge v-if="run.degradedFlags.length > 0" variant="outline" class="text-[10px] px-1 py-0 text-amber-600 border-amber-500/50">
                降级 {{ run.degradedFlags.length }}
              </Badge>
              <Badge v-if="run.qualityIssues.length > 0" variant="outline" class="text-[10px] px-1 py-0 text-orange-600 border-orange-500/50">
                问题 {{ run.qualityIssues.length }}
              </Badge>
              <ChevronRight
                class="h-3.5 w-3.5 ml-auto text-muted-foreground flex-shrink-0 transition-transform"
                :class="{ 'rotate-90': expandedRun === (run.runId || run.traceId) }"
              />
            </button>
            <div v-if="expandedRun === (run.runId || run.traceId)" class="px-3 pb-3 space-y-2 border-t border-border/40 pt-2">
              <div class="text-[11px] text-muted-foreground">
                TraceId: {{ run.traceId }}<span v-if="run.pipelineVersion"> · 版本: {{ run.pipelineVersion }}</span>
              </div>
              <div class="space-y-0.5">
                <div
                  v-for="(step, si) in run.steps"
                  :key="si"
                  class="flex items-center gap-2 text-xs"
                >
                  <span class="text-muted-foreground w-4 text-right flex-shrink-0">{{ si + 1 }}</span>
                  <span class="w-32 flex-shrink-0">{{ stageLabel(step.stage) }}</span>
                  <span class="w-20 flex-shrink-0" :class="runStatusClass(step.status)">{{ step.status }}</span>
                  <span class="text-muted-foreground">{{ step.durationMs ?? 0 }}ms</span>
                  <span v-if="step.errorCode" class="text-amber-600 truncate" :title="degradedLabel(step.errorCode)">
                    {{ degradedLabel(step.errorCode) }}
                  </span>
                </div>
              </div>
              <template v-if="run.qualityIssues.length > 0">
                <Separator />
                <div class="space-y-1">
                  <div
                    v-for="(issue, ii) in run.qualityIssues"
                    :key="ii"
                    class="text-xs bg-muted/50 rounded px-2 py-1"
                  >
                    <span class="text-orange-600 font-medium">[{{ issue.riskLevel }}] {{ issue.issueType }}</span>
                    <span class="text-foreground/80"> {{ issue.description }}</span>
                    <span v-if="issue.currentCode" class="text-muted-foreground">（编码 {{ issue.currentCode }}）</span>
                  </div>
                </div>
              </template>
            </div>
            </div>
          </template>
        </div>
      </ScrollArea>
    </SheetContent>
  </Sheet>
</template>

<style scoped>
.no-scrollbar::-webkit-scrollbar {
  display: none;
}

.no-scrollbar {
  scrollbar-width: none;
}
</style>
