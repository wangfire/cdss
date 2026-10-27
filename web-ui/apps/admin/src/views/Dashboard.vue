<script setup lang="ts">
import {
  Activity,
  AlertCircle,
  ArrowDownRight,
  ArrowRight,
  ArrowUpRight,
  BarChart3,
  BookOpen,
  ClipboardList,
  FilePlus2,
  KeyRound,
  Library,
  Loader2,
  LogOut,
  PieChart,
  Server,
  TrendingUp,
} from 'lucide-vue-next'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useUserStore } from '@/stores/user'
import {
  CASE_ENTRY_PERMISSIONS,
  KNOWLEDGE_PERMISSIONS,
  WORKBENCH_PERMISSIONS,
} from '@/constants/permissions'
import { ApiError, authApi, healthApi, knowledgeApi, workbenchApi } from '@/api'
import type { WorkbenchTaskResponse } from '@/api'
import {
  Avatar,
  AvatarFallback,
  AvatarImage,
  Badge,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  Input,
  Label,
  toast,
} from '@tabtab/ui'

const { t } = useI18n()
const userStore = useUserStore()
const router = useRouter()

/* ============== 用户信息 ============== */
const currentUserName = computed(() => userStore.user?.userName || 'Admin')
const currentUserDisplay = computed(
  () => userStore.user?.displayName || userStore.user?.userName || 'Admin',
)
const currentUserAvatar = computed(() => userStore.defaultAvatar)
const currentUserRoles = computed<string[]>(() => userStore.roles || [])
const userInitials = computed(() => {
  const name = currentUserDisplay.value
  if (!name) return 'U'
  return name.trim().charAt(0).toUpperCase()
})

/* ============== 修改密码 ============== */
const pwdOpen = ref(false)
const pwdSaving = ref(false)
const pwdForm = ref({ oldPassword: '', newPassword: '', confirmNewPassword: '' })

function resetPwdForm() {
  pwdForm.value = { oldPassword: '', newPassword: '', confirmNewPassword: '' }
}
function openChangePassword() { resetPwdForm(); pwdOpen.value = true }

const pwdMismatch = computed(
  () => pwdForm.value.confirmNewPassword.length > 0 && pwdForm.value.newPassword !== pwdForm.value.confirmNewPassword,
)
const pwdTooShort = computed(() => pwdForm.value.newPassword.length > 0 && pwdForm.value.newPassword.length < 8)
const pwdFormValid = computed(
  () => pwdForm.value.oldPassword.length > 0 && pwdForm.value.newPassword.length >= 8 && pwdForm.value.newPassword === pwdForm.value.confirmNewPassword,
)

async function handleChangePassword() {
  if (pwdSaving.value) return
  if (!pwdForm.value.oldPassword) { toast.warning(t('common.oldPassword')); return }
  if (pwdForm.value.newPassword.length < 8) { toast.warning(t('common.passwordTooShort')); return }
  if (pwdForm.value.newPassword !== pwdForm.value.confirmNewPassword) { toast.warning(t('common.passwordMismatch')); return }
  pwdSaving.value = true
  try {
    await authApi.changePassword({ oldPassword: pwdForm.value.oldPassword, newPassword: pwdForm.value.newPassword })
    pwdOpen.value = false
    resetPwdForm()
    toast.success(t('common.changePasswordSuccess'))
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message || t('common.changePasswordFailed') : t('common.changePasswordFailed'))
  }
  finally { pwdSaving.value = false }
}

/* ============== 登出 ============== */
const loggingOut = ref(false)
async function handleLogout() {
  if (loggingOut.value) return
  loggingOut.value = true
  try { await userStore.logout() } finally { loggingOut.value = false; router.push({ name: 'Login' }) }
}

/* ============== 日期与问候 ============== */
const currentDate = computed(() => new Date().toLocaleDateString('zh-CN', { year: 'numeric', month: 'long', day: 'numeric', weekday: 'long' }))
const greeting = computed(() => {
  const h = new Date().getHours()
  if (h < 12) return t('common.greeting.morning')
  if (h < 18) return t('common.greeting.afternoon')
  return t('common.greeting.evening')
})

/* ============== 真实 CDSS 数据 ============== */
const loading = ref(true)
const healthStatus = ref<string>('unknown')
const serviceChecks = ref<Record<string, { status: string; error?: string | null }>>({})

// readiness 契约：healthy / degraded / unhealthy；网络失败时为 unknown
const HEALTH_LABEL_KEYS: Record<string, string> = {
  healthy: 'dashboard.operational',
  degraded: 'dashboard.degraded',
  unhealthy: 'dashboard.unhealthy',
  unknown: 'dashboard.statusUnknown',
}
const healthLabel = (status: string) => {
  const key = HEALTH_LABEL_KEYS[status.toLowerCase()]
  return key ? t(key) : status
}
const isHealthy = computed(() => healthStatus.value.toLowerCase() === 'healthy')
const DEPENDENCY_LABEL_KEYS: Record<string, string> = {
  sqlServer: 'dashboard.database',
  rabbitMq: 'dashboard.messageQueue',
  elasticsearch: 'dashboard.searchEngine',
}
const dependencyLabel = (name: string) => {
  const key = DEPENDENCY_LABEL_KEYS[name]
  return key ? t(key) : name
}
const tasks = ref<WorkbenchTaskResponse[]>([])

const statusCounts = computed(() => {
  const counts: Record<string, number> = {}
  for (const task of tasks.value) {
    counts[task.status] = (counts[task.status] || 0) + 1
  }
  return counts
})

const pendingReviewCount = computed(() =>
  (statusCounts.value['HUMAN_REQUIRED'] || 0) + (statusCounts.value['PENDING_REVIEW'] || 0),
)

const acceptedCount = computed(() =>
  (statusCounts.value['ACCEPTED'] || 0) + (statusCounts.value['MODIFIED'] || 0),
)

/* ============== 概览指标与图表 ============== */
const totalRecommendations = computed(() =>
  tasks.value.reduce((sum, task) => sum + (task.recommendationCount || 0), 0),
)
const pendingRate = computed(() =>
  tasks.value.length ? Math.round((pendingReviewCount.value / tasks.value.length) * 100) : 0,
)
const completionRate = computed(() =>
  tasks.value.length ? Math.round((acceptedCount.value / tasks.value.length) * 100) : 0,
)
const healthyChecks = computed(
  () => Object.values(serviceChecks.value).filter(check => check.status === 'healthy').length,
)
const checkCount = computed(() => Object.keys(serviceChecks.value).length)

const kpiTargets = computed(() => ({
  total: tasks.value.length,
  pending: pendingReviewCount.value,
  done: acceptedCount.value,
}))
const kpiDisplay = ref({ total: 0, pending: 0, done: 0 })
let kpiFrame = 0
watch(kpiTargets, (target) => {
  cancelAnimationFrame(kpiFrame)
  const from = { ...kpiDisplay.value }
  const started = performance.now()
  const tick = (now: number) => {
    const progress = Math.min(1, (now - started) / 720)
    const eased = 1 - Math.pow(1 - progress, 3)
    kpiDisplay.value = {
      total: Math.round(from.total + (target.total - from.total) * eased),
      pending: Math.round(from.pending + (target.pending - from.pending) * eased),
      done: Math.round(from.done + (target.done - from.done) * eased),
    }
    if (progress < 1) kpiFrame = requestAnimationFrame(tick)
  }
  kpiFrame = requestAnimationFrame(tick)
}, { immediate: true })
onBeforeUnmount(() => cancelAnimationFrame(kpiFrame))

const DAY_MS = 24 * 60 * 60 * 1000
const WEEKDAYS = computed(() => [
  t('dashboard.sun'),
  t('dashboard.mon'),
  t('dashboard.tue'),
  t('dashboard.wed'),
  t('dashboard.thu'),
  t('dashboard.fri'),
  t('dashboard.sat'),
])
const localDayKey = (date: Date) => `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`

/** 近 7 日无新增时回溯到最近活跃日，避免图表整片空白。 */
const trendEnd = computed(() => {
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const recentStart = today.getTime() - 6 * DAY_MS
  const hasRecent = tasks.value.some(task => new Date(task.createdAt).getTime() >= recentStart)
  if (hasRecent || tasks.value.length === 0) return { end: today, recent: true }
  let latest = 0
  for (const task of tasks.value) {
    latest = Math.max(latest, new Date(task.createdAt).getTime())
  }
  const end = new Date(latest)
  end.setHours(0, 0, 0, 0)
  return { end, recent: false }
})
const trendDays = computed(() => {
  const counts = new Map<string, number>()
  for (const task of tasks.value) {
    const key = localDayKey(new Date(task.createdAt))
    counts.set(key, (counts.get(key) || 0) + 1)
  }
  const end = trendEnd.value.end
  return Array.from({ length: 7 }, (_, index) => {
    const day = new Date(end.getTime() - (6 - index) * DAY_MS)
    return {
      label: `${day.getMonth() + 1}/${day.getDate()}`,
      weekday: WEEKDAYS.value[day.getDay()],
      count: counts.get(localDayKey(day)) || 0,
    }
  })
})
const trendMax = computed(() => Math.max(1, ...trendDays.value.map(day => day.count)))
const trendBarHeight = (count: number) =>
  count === 0 ? 3 : Math.max(10, Math.round((count / trendMax.value) * 100))
const last7Count = computed(() => trendDays.value.reduce((sum, day) => sum + day.count, 0))
const prev7Count = computed(() => {
  const end = trendEnd.value.end.getTime() - 6 * DAY_MS
  const start = end - 7 * DAY_MS
  return tasks.value.filter((task) => {
    const time = new Date(task.createdAt).getTime()
    return time >= start && time < end
  }).length
})
const weekDelta = computed(() => {
  if (prev7Count.value === 0) return last7Count.value > 0 ? 100 : 0
  return Math.round(((last7Count.value - prev7Count.value) / prev7Count.value) * 100)
})
const sparkPoints = computed(() =>
  trendDays.value
    .map((day, index) => `${(index / 6) * 100},${26 - (day.count / trendMax.value) * 22}`)
    .join(' '),
)

const DONUT_C = 2 * Math.PI * 46
const donutBuckets = computed(() => {
  const defs = [
    { key: 'review', label: t('dashboard.metrics.pendingReview'), color: '#8b5cf6', statuses: ['HUMAN_REQUIRED', 'PENDING_REVIEW'] },
    { key: 'done', label: t('dashboard.metrics.accepted'), color: '#10b981', statuses: ['ACCEPTED', 'MODIFIED'] },
    { key: 'running', label: t('dashboard.metrics.processing'), color: '#3b82f6', statuses: ['PENDING', 'RUNNING', 'RETRYING'] },
    { key: 'failed', label: t('dashboard.metrics.abnormal'), color: '#ef4444', statuses: ['FAILED', 'TIMEOUT', 'CANCELLED', 'REJECTED'] },
  ]
  const total = tasks.value.length
  return defs
    .map(def => {
      const count = def.statuses.reduce((sum, status) => sum + (statusCounts.value[status] || 0), 0)
      return { ...def, count, pct: total ? (count / total) * 100 : 0 }
    })
    .filter(bucket => bucket.count > 0)
})
const donutSegments = computed(() => {
  const total = Math.max(1, tasks.value.length)
  let offset = 0
  return donutBuckets.value.map((bucket) => {
    const length = (bucket.count / total) * DONUT_C
    const gap = donutBuckets.value.length > 1 ? 2.5 : 0
    const segment = { ...bucket, len: Math.max(0, length - gap), offset }
    offset += length
    return segment
  })
})

/** 数据到位后再从 0 生长，避免首次绘制直接是终态。 */
const chartsReady = ref(false)
watch(tasks, async () => {
  chartsReady.value = false
  await nextTick()
  requestAnimationFrame(() => { chartsReady.value = true })
})

/* ============== 编码工作量补充 ============== */
const pendingRecommendations = computed(() =>
  tasks.value
    .filter(task => task.status === 'HUMAN_REQUIRED' || task.status === 'PENDING_REVIEW')
    .reduce((sum, task) => sum + (task.recommendationCount || 0), 0),
)
const avgRecommendations = computed(() =>
  tasks.value.length ? totalRecommendations.value / tasks.value.length : 0,
)

const REC_BUCKETS = [
  { key: 'lt20', label: '<20', min: 0, max: 19 },
  { key: '20s', label: '20-29', min: 20, max: 29 },
  { key: '30s', label: '30-39', min: 30, max: 39 },
  { key: '40s', label: '40-49', min: 40, max: 49 },
  { key: 'gte50', label: '≥50', min: 50, max: Number.POSITIVE_INFINITY },
]
const recDistribution = computed(() =>
  REC_BUCKETS.map((bucket) => {
    const count = tasks.value.filter((task) => {
      const value = task.recommendationCount || 0
      return value >= bucket.min && value <= bucket.max
    }).length
    return { ...bucket, count }
  }),
)
const recDistMax = computed(() => Math.max(1, ...recDistribution.value.map(bucket => bucket.count)))

/* ============== 知识库概况 ============== */
const canReadKnowledge = computed(() =>
  userStore.hasAnyPermission(['coding.read', ...KNOWLEDGE_PERMISSIONS]),
)
const knowledgeLoading = ref(false)
const knowledgeSystems = ref<string[]>([])
const knowledgeCounts = ref<{
  codeSystems: number
  medicalCodes: number
  icd: number
  icdValid: number
  cm3: number
  cm3Valid: number
  tumor: number
  synonyms: number
  rules: number
} | null>(null)

async function loadKnowledgeStats() {
  if (!canReadKnowledge.value) return
  knowledgeLoading.value = true
  try {
    const [systems, medical, icd, icdValid, cm3, cm3Valid, tumor, synonyms, rules] =
      await Promise.all([
        knowledgeApi.getCodeSystems(),
        knowledgeApi.getMedicalCodes({ page: 1, pageSize: 1 }),
        knowledgeApi.getIcdIndex({ page: 1, pageSize: 1 }),
        knowledgeApi.getIcdIndex({ page: 1, pageSize: 1, leadWordStatus: 0 }),
        knowledgeApi.getCm3Index({ page: 1, pageSize: 1 }),
        knowledgeApi.getCm3Index({ page: 1, pageSize: 1, leadWordStatus: 0 }),
        knowledgeApi.getTumorIndex({ page: 1, pageSize: 1 }),
        knowledgeApi.getTermSynonymRows({ page: 1, pageSize: 1 }),
        knowledgeApi.getCodingRules({ page: 1, pageSize: 1 }),
      ])
    knowledgeSystems.value = Array.from(new Set(systems.map(item => `${item.code}@${item.version}`)))
    knowledgeCounts.value = {
      codeSystems: new Set(systems.map(item => item.code)).size,
      medicalCodes: medical.total,
      icd: icd.total,
      icdValid: icdValid.total,
      cm3: cm3.total,
      cm3Valid: cm3Valid.total,
      tumor: tumor.total,
      synonyms: synonyms.total,
      rules: rules.total,
    }
  }
  catch {
    // 知识库统计失败不影响仪表盘其他指标展示。
  }
  finally {
    knowledgeLoading.value = false
  }
}

const knowledgeStats = computed(() => {
  const counts = knowledgeCounts.value
  if (!counts) return []
  const items = [
    { key: 'icd', label: t('dashboard.knowledge.icd'), count: counts.icd, hint: t('dashboard.knowledge.validLead', { count: counts.icdValid }), color: 'bg-violet-500' },
    { key: 'cm3', label: t('dashboard.knowledge.cm3'), count: counts.cm3, hint: t('dashboard.knowledge.validLead', { count: counts.cm3Valid }), color: 'bg-blue-500' },
    { key: 'tumor', label: t('dashboard.knowledge.tumor'), count: counts.tumor, hint: '', color: 'bg-rose-500' },
    { key: 'synonyms', label: t('dashboard.knowledge.synonyms'), count: counts.synonyms, hint: '', color: 'bg-emerald-500' },
    { key: 'rules', label: t('dashboard.knowledge.rules'), count: counts.rules, hint: '', color: 'bg-amber-500' },
    { key: 'medicalCodes', label: t('dashboard.knowledge.medicalCodes'), count: counts.medicalCodes, hint: '', color: 'bg-sky-500' },
  ]
  const max = Math.max(1, ...items.map(item => item.count))
  // 数量级差异大（千 vs 十万），用平方根压缩后仍能看出相对规模。
  return items.map(item => ({ ...item, width: Math.sqrt(item.count / max) * 100 }))
})

const knowledgeDisplay = ref<Record<string, number>>({})
let knowledgeFrame = 0
watch(knowledgeStats, (stats) => {
  cancelAnimationFrame(knowledgeFrame)
  if (stats.length === 0) return
  const from: Record<string, number> = {}
  for (const item of stats) from[item.key] = knowledgeDisplay.value[item.key] ?? 0
  const started = performance.now()
  const tick = (now: number) => {
    const progress = Math.min(1, (now - started) / 720)
    const eased = 1 - Math.pow(1 - progress, 3)
    const next: Record<string, number> = {}
    for (const item of stats) {
      next[item.key] = Math.round(from[item.key]! + (item.count - from[item.key]!) * eased)
    }
    knowledgeDisplay.value = next
    if (progress < 1) knowledgeFrame = requestAnimationFrame(tick)
  }
  knowledgeFrame = requestAnimationFrame(tick)
}, { immediate: true })
onBeforeUnmount(() => cancelAnimationFrame(knowledgeFrame))

/** 审核权限隐含编码查看能力，与后端 CodingReader 策略保持一致。 */
const canReadCoding = computed(() => userStore.hasAnyPermission(WORKBENCH_PERMISSIONS))

async function loadData() {
  loading.value = true
  try {
    const [health, taskList] = await Promise.allSettled([
      healthApi.readiness(),
      canReadCoding.value ? workbenchApi.listTasks() : Promise.resolve<WorkbenchTaskResponse[]>([]),
    ])
    if (health.status === 'fulfilled') {
      healthStatus.value = health.value.status
      serviceChecks.value = health.value.dependencies || {}
    }
    if (taskList.status === 'fulfilled') {
      tasks.value = taskList.value
    }
  }
  finally {
    loading.value = false
  }
}

onMounted(() => {
  loadData()
  loadKnowledgeStats()
})

const quickActions = computed(() => [
  ...(canReadCoding.value
    ? [{
        id: '1',
        label: t('workbench.title'),
        icon: ClipboardList,
        onClick: () => router.push({ name: 'Workbench' }),
        variant: 'primary' as const,
      }]
    : []),
  ...(userStore.hasAnyPermission(CASE_ENTRY_PERMISSIONS)
    ? [{
        id: '3',
        label: t('menu.caseEntry'),
        icon: FilePlus2,
        onClick: () => router.push({ name: 'CaseEntry' }),
        variant: 'default' as const,
      }]
    : []),
  ...(canReadKnowledge.value
    ? [{
        id: '2',
        label: t('knowledge.title'),
        icon: BookOpen,
        onClick: () => router.push({ name: 'Knowledge' }),
        variant: 'default' as const,
      }]
    : []),
])
</script>

<template>
  <div class="space-y-6">
    <!-- 页面标题 -->
    <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
      <div>
        <h1 class="text-2xl font-bold text-foreground">{{ t('dashboard.dashboard') }}</h1>
        <p class="text-muted-foreground mt-1">
          {{ greeting }}，{{ currentUserDisplay }}！{{ currentDate }}
        </p>
      </div>
      <div class="flex items-center gap-2">
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <Button variant="ghost" size="icon" class="h-9 w-9 rounded-full p-0 overflow-hidden border border-border/60 hover:border-border" :title="currentUserDisplay">
              <Avatar class="h-9 w-9">
                <AvatarImage :src="currentUserAvatar" :alt="currentUserDisplay" />
                <AvatarFallback>{{ userInitials }}</AvatarFallback>
              </Avatar>
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" class="w-64 p-0">
            <DropdownMenuLabel class="p-3">
              <div class="flex items-center gap-3">
                <Avatar class="h-10 w-10">
                  <AvatarImage :src="currentUserAvatar" :alt="currentUserDisplay" />
                  <AvatarFallback>{{ userInitials }}</AvatarFallback>
                </Avatar>
                <div class="flex-1 min-w-0">
                  <div class="text-sm font-semibold text-foreground truncate">{{ currentUserDisplay }}</div>
                  <div class="text-xs text-muted-foreground truncate">@{{ currentUserName }}</div>
                  <div v-if="currentUserRoles.length" class="flex flex-wrap gap-1 mt-1.5">
                    <Badge v-for="r in currentUserRoles" :key="r" variant="secondary" class="text-[10px] px-1.5 py-0">{{ r }}</Badge>
                  </div>
                </div>
              </div>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuItem class="cursor-pointer" @select="openChangePassword">
              <KeyRound class="h-4 w-4" />
              <span>{{ t('common.changePassword') }}</span>
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem variant="destructive" class="cursor-pointer" :disabled="loggingOut" @select="handleLogout">
              <Loader2 v-if="loggingOut" class="h-4 w-4 animate-spin" />
              <LogOut v-else class="h-4 w-4" />
              <span>{{ t('common.logout') }}</span>
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </div>

    <!-- 核心指标卡片 -->
    <div
      class="grid grid-cols-1 sm:grid-cols-2 gap-4"
      :class="canReadCoding ? 'lg:grid-cols-4' : 'lg:grid-cols-2'"
    >
      <template v-if="canReadCoding">
      <Card class="dash-kpi dash-shine group relative overflow-hidden border-border/40">
        <CardContent class="relative p-5">
          <div class="flex items-start justify-between">
            <div class="flex-1 min-w-0">
              <div class="flex items-center gap-2 mb-3">
                <div class="dash-kpi-icon p-2 rounded-lg bg-violet-500/10">
                  <ClipboardList class="h-4 w-4 text-violet-500" />
                </div>
                <span class="text-sm font-medium text-muted-foreground">{{ t('workbench.title') }}</span>
              </div>
              <div class="text-2xl font-bold tracking-tight tabular-nums">{{ kpiDisplay.total }}</div>
              <p class="text-xs text-muted-foreground truncate">
                {{ t('dashboard.metrics.totalTasks') }} · {{ t('dashboard.charts.recommendationTotal', { count: totalRecommendations }) }}
              </p>
            </div>
          </div>
          <svg viewBox="0 0 100 28" preserveAspectRatio="none" class="mt-3 h-7 w-full text-violet-500/80" aria-hidden="true">
            <polyline
              :points="sparkPoints"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
              class="dash-spark"
              :style="{ strokeDashoffset: chartsReady ? 0 : 400 }"
            />
          </svg>
        </CardContent>
      </Card>

      <Card class="dash-kpi dash-shine group relative overflow-hidden border-border/40">
        <CardContent class="relative p-5">
          <div class="flex items-start justify-between">
            <div class="flex-1 min-w-0">
              <div class="flex items-center gap-2 mb-3">
                <div class="dash-kpi-icon p-2 rounded-lg bg-amber-500/10">
                  <AlertCircle class="h-4 w-4 text-amber-500" />
                </div>
                <span class="text-sm font-medium text-muted-foreground">{{ t('dashboard.pendingReview') }}</span>
              </div>
              <div class="text-2xl font-bold tracking-tight tabular-nums text-violet-600">{{ kpiDisplay.pending }}</div>
              <p class="text-xs text-muted-foreground truncate">{{ t('workbench.statusHumanRequired') }} / {{ t('workbench.statusPendingReview') }}</p>
            </div>
          </div>
          <div class="mt-3.5 flex items-center gap-2">
            <div class="h-1.5 flex-1 overflow-hidden rounded-full bg-muted">
              <div class="dash-bar h-full rounded-full bg-violet-500" :style="{ width: chartsReady ? `${pendingRate}%` : '0%' }" />
            </div>
            <span class="shrink-0 text-[10px] tabular-nums text-muted-foreground">{{ t('dashboard.charts.pendingRate', { rate: pendingRate }) }}</span>
          </div>
        </CardContent>
      </Card>

      <Card class="dash-kpi dash-shine group relative overflow-hidden border-border/40">
        <CardContent class="relative p-5">
          <div class="flex items-start justify-between">
            <div class="flex-1 min-w-0">
              <div class="flex items-center gap-2 mb-3">
                <div class="dash-kpi-icon p-2 rounded-lg bg-emerald-500/10">
                  <Activity class="h-4 w-4 text-emerald-500" />
                </div>
                <span class="text-sm font-medium text-muted-foreground">{{ t('dashboard.completed') }}</span>
              </div>
              <div class="text-2xl font-bold tracking-tight tabular-nums text-emerald-600">{{ kpiDisplay.done }}</div>
              <p class="text-xs text-muted-foreground truncate">{{ t('workbench.statusAccepted') }} / {{ t('workbench.statusModified') }}</p>
            </div>
          </div>
          <div class="mt-3.5 flex items-center gap-2">
            <div class="h-1.5 flex-1 overflow-hidden rounded-full bg-muted">
              <div class="dash-bar h-full rounded-full bg-emerald-500" :style="{ width: chartsReady ? `${completionRate}%` : '0%' }" />
            </div>
            <span class="shrink-0 text-[10px] tabular-nums text-muted-foreground">{{ t('dashboard.charts.completionRate', { rate: completionRate }) }}</span>
          </div>
        </CardContent>
      </Card>
      </template>

      <Card class="dash-kpi dash-shine group relative overflow-hidden border-border/40">
        <CardContent class="relative p-5">
          <div class="flex items-start justify-between">
            <div class="flex-1 min-w-0">
              <div class="flex items-center gap-2 mb-3">
                <div class="dash-kpi-icon p-2 rounded-lg bg-blue-500/10">
                  <Server class="h-4 w-4 text-blue-500" />
                </div>
                <span class="text-sm font-medium text-muted-foreground">{{ t('dashboard.systemStatus') }}</span>
              </div>
              <div class="text-2xl font-bold tracking-tight mb-1">
                <Badge :variant="isHealthy ? 'default' : 'destructive'" class="text-sm">
                  {{ healthLabel(healthStatus) }}
                </Badge>
              </div>
              <p class="text-xs text-muted-foreground truncate">
                {{ Object.keys(serviceChecks).length }} {{ t('dashboard.apiService') }}
              </p>
            </div>
          </div>
          <div class="mt-4 flex items-center gap-1.5" :title="t('dashboard.charts.healthyDeps', { ok: healthyChecks, total: checkCount })">
            <span
              v-for="(check, name) in serviceChecks"
              :key="name"
              class="dash-dot h-1.5 w-1.5 rounded-full"
              :class="check.status === 'healthy' ? 'bg-emerald-500' : check.status === 'disabled' ? 'bg-slate-400' : 'bg-red-500'"
            />
            <span class="ml-1 text-[10px] tabular-nums text-muted-foreground">{{ t('dashboard.charts.healthyDeps', { ok: healthyChecks, total: checkCount }) }}</span>
          </div>
        </CardContent>
      </Card>
    </div>

    <!-- 主要内容 -->
    <div class="grid grid-cols-1 gap-6" :class="canReadCoding ? 'lg:grid-cols-3' : 'lg:grid-cols-1'">
      <!-- 左侧：快捷操作 + 系统状态 -->
      <div class="space-y-6">
        <Card v-if="quickActions.length > 0" class="dash-rise border border-border/40" style="animation-delay: 40ms">
          <CardHeader class="pb-3">
            <CardTitle class="text-base font-semibold">{{ t('dashboard.quickEntry') }}</CardTitle>
          </CardHeader>
          <CardContent class="pt-0">
            <div class="grid grid-cols-2 gap-3">
              <Button
                v-for="action in quickActions"
                :key="action.id"
                class="h-auto py-4 px-3 flex flex-col items-center gap-2 rounded-xl transition-all duration-200 border-0 hover:-translate-y-0.5"
                :class="[action.variant === 'primary' ? 'bg-primary hover:bg-primary/90 text-primary-foreground' : 'bg-muted hover:bg-muted/80 text-muted-foreground hover:text-foreground']"
                @click="action.onClick"
              >
                <div v-if="action.icon" class="p-2 rounded-lg" :class="[action.variant === 'primary' ? 'bg-white/20' : 'bg-background/50']">
                  <component :is="action.icon" class="h-5 w-5" />
                </div>
                <span class="text-sm font-medium">{{ action.label }}</span>
              </Button>
            </div>
          </CardContent>
        </Card>

        <Card class="dash-rise bg-muted/40 border border-border/50 rounded-xl" style="animation-delay: 90ms">
          <CardHeader class="pb-4">
            <div class="flex items-center justify-between">
              <CardTitle class="text-base">{{ t('dashboard.systemStatus') }}</CardTitle>
              <Badge variant="outline" :class="isHealthy ? 'text-emerald-500 border-emerald-500/20 bg-emerald-500/10' : 'text-red-500 border-red-500/20 bg-red-500/10'">
                <div class="w-1.5 h-1.5 mr-1.5 animate-pulse rounded-full" :class="isHealthy ? 'bg-emerald-500' : 'bg-red-500'" />
                {{ healthLabel(healthStatus) }}
              </Badge>
            </div>
          </CardHeader>
          <CardContent class="pt-0">
            <div class="space-y-3">
              <div v-for="(check, name) in serviceChecks" :key="name" class="flex items-center justify-between p-3 bg-muted/50 hover:bg-muted transition-colors rounded-lg">
                <div class="flex items-center gap-3">
                  <div class="w-2 h-2 rounded-full" :class="check.status === 'healthy' ? 'bg-emerald-500' : check.status === 'disabled' ? 'bg-slate-400' : 'bg-red-500'" />
                  <span class="text-sm font-medium">{{ dependencyLabel(String(name)) }}</span>
                </div>
                <span class="text-xs text-muted-foreground" :title="check.error || undefined">{{ healthLabel(check.status) }}</span>
              </div>
              <div v-if="Object.keys(serviceChecks).length === 0" class="text-sm text-muted-foreground text-center py-4">
                {{ t('common.loading') }}
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- 中间：图表 -->
      <div v-if="canReadCoding" class="lg:col-span-2 space-y-6">
        <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
          <!-- 任务状态分布：环形图 -->
          <Card class="dash-rise bg-muted/40 border border-border/50 rounded-xl" style="animation-delay: 40ms">
            <CardHeader class="pb-2">
              <CardTitle class="flex items-center gap-2 text-base">
                <PieChart class="h-4 w-4 text-violet-500" />
                {{ t('dashboard.charts.statusTitle') }}
              </CardTitle>
            </CardHeader>
            <CardContent class="pt-1">
              <div class="flex items-center gap-4">
                <div class="relative h-28 w-28 shrink-0">
                  <svg viewBox="0 0 120 120" class="h-full w-full -rotate-90">
                    <circle cx="60" cy="60" r="46" fill="none" stroke="currentColor" stroke-width="14" class="text-muted/50" />
                    <circle
                      v-for="seg in donutSegments"
                      :key="seg.key"
                      cx="60"
                      cy="60"
                      r="46"
                      fill="none"
                      :stroke="seg.color"
                      stroke-width="14"
                      :stroke-dasharray="chartsReady ? `${seg.len} ${DONUT_C - seg.len}` : `0 ${DONUT_C}`"
                      :stroke-dashoffset="chartsReady ? -seg.offset : 0"
                      class="dash-donut-seg"
                    />
                  </svg>
                  <div class="absolute inset-0 flex flex-col items-center justify-center">
                    <span class="text-xl font-bold tabular-nums">{{ kpiDisplay.total }}</span>
                    <span class="text-[10px] text-muted-foreground">{{ t('dashboard.metrics.totalTasks') }}</span>
                  </div>
                </div>
                <div class="min-w-0 flex-1 space-y-1.5">
                  <div v-for="bucket in donutBuckets" :key="bucket.key" class="flex items-center gap-2 text-xs">
                    <span class="h-2 w-2 shrink-0 rounded-full" :style="{ backgroundColor: bucket.color }" />
                    <span class="text-muted-foreground">{{ bucket.label }}</span>
                    <span class="ml-auto font-medium tabular-nums">{{ bucket.count }}</span>
                    <span class="w-9 shrink-0 text-right text-[10px] tabular-nums text-muted-foreground">{{ bucket.pct.toFixed(1) }}%</span>
                  </div>
                  <p v-if="donutBuckets.length === 0" class="text-xs text-muted-foreground">{{ t('dashboard.charts.noTasks') }}</p>
                </div>
              </div>

              <div class="mt-4 grid grid-cols-2 gap-2">
                <div class="rounded-lg bg-background/60 px-3 py-2">
                  <p class="text-sm font-semibold tabular-nums">{{ pendingRecommendations.toLocaleString('zh-CN') }}</p>
                  <p class="text-[10px] text-muted-foreground">{{ t('dashboard.charts.pendingRecs') }}</p>
                </div>
                <div class="rounded-lg bg-background/60 px-3 py-2">
                  <p class="text-sm font-semibold tabular-nums">{{ avgRecommendations.toFixed(1) }}</p>
                  <p class="text-[10px] text-muted-foreground">{{ t('dashboard.charts.avgRecs') }}</p>
                </div>
              </div>
            </CardContent>
          </Card>

          <!-- 近 7 日任务量：柱状趋势 -->
          <Card class="dash-rise bg-muted/40 border border-border/50 rounded-xl" style="animation-delay: 90ms">
            <CardHeader class="pb-2">
              <div class="flex items-center justify-between gap-2">
                <CardTitle class="flex items-center gap-2 text-base">
                  <TrendingUp class="h-4 w-4 text-blue-500" />
                  {{ trendEnd.recent ? t('dashboard.charts.trendTitle') : t('dashboard.charts.trendFallbackTitle') }}
                </CardTitle>
                <Badge
                  variant="outline"
                  class="gap-0.5 text-[10px]"
                  :class="weekDelta >= 0 ? 'text-emerald-600 border-emerald-500/40' : 'text-red-600 border-red-500/40'"
                >
                  <ArrowUpRight v-if="weekDelta >= 0" class="h-3 w-3" />
                  <ArrowDownRight v-else class="h-3 w-3" />
                  {{ weekDelta >= 0 ? '+' : '' }}{{ weekDelta }}%
                </Badge>
              </div>
            </CardHeader>
            <CardContent class="pt-1">
              <div class="flex h-[104px] items-end gap-1.5">
                <div
                  v-for="(day, idx) in trendDays"
                  :key="day.label"
                  class="group flex h-full flex-1 flex-col items-center justify-end gap-1"
                >
                  <span class="text-[10px] font-medium tabular-nums text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100">{{ day.count }}</span>
                  <div class="relative w-full flex-1">
                    <div
                      class="dash-bar absolute inset-x-0 bottom-0 rounded-t-md bg-gradient-to-t from-blue-500/60 to-violet-500/90"
                      :style="{ height: chartsReady ? `${trendBarHeight(day.count)}%` : '0%', transitionDelay: `${idx * 45}ms` }"
                      :title="`${day.label} · ${day.count}`"
                    />
                  </div>
                  <span class="text-[10px] text-muted-foreground">{{ day.weekday }}</span>
                </div>
              </div>
              <div class="mt-2 flex items-center justify-between text-[10px] text-muted-foreground">
                <span>{{ t('dashboard.charts.weekTotal') }} <strong class="tabular-nums text-foreground">{{ last7Count }}</strong></span>
                <span>{{ t('dashboard.charts.prevWeek') }} <strong class="tabular-nums text-foreground">{{ prev7Count }}</strong></span>
              </div>
              <p v-if="!trendEnd.recent" class="mt-0.5 text-[10px] text-muted-foreground/70">{{ t('dashboard.charts.trendHint') }}</p>
            </CardContent>
          </Card>

          <!-- 每条任务推荐量分布 -->
          <Card class="dash-rise bg-muted/40 border border-border/50 rounded-xl md:col-span-2" style="animation-delay: 140ms">
            <CardHeader class="pb-2">
              <div class="flex items-center justify-between gap-2">
                <CardTitle class="flex items-center gap-2 text-base">
                  <BarChart3 class="h-4 w-4 text-emerald-500" />
                  {{ t('dashboard.charts.distTitle') }}
                </CardTitle>
                <span class="text-[11px] text-muted-foreground">{{ t('dashboard.charts.distTotal', { count: tasks.length }) }}</span>
              </div>
            </CardHeader>
            <CardContent class="pt-1">
              <div class="flex h-[104px] items-end gap-3">
                <div
                  v-for="(bucket, idx) in recDistribution"
                  :key="bucket.key"
                  class="group flex h-full flex-1 flex-col items-center justify-end gap-1"
                >
                  <span class="text-[10px] font-medium tabular-nums text-muted-foreground">{{ bucket.count }}</span>
                  <div class="relative w-full flex-1">
                    <div
                      class="dash-bar absolute inset-x-0 bottom-0 rounded-t-md bg-gradient-to-t from-emerald-500/50 to-teal-500/90 transition-opacity group-hover:opacity-80"
                      :style="{ height: chartsReady ? `${Math.max(3, (bucket.count / recDistMax) * 100)}%` : '0%', transitionDelay: `${idx * 60}ms` }"
                      :title="`${bucket.label}: ${bucket.count}`"
                    />
                  </div>
                  <span class="text-[10px] text-muted-foreground">{{ bucket.label }}</span>
                </div>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>

    <!-- 知识库概况 -->
    <div v-if="canReadKnowledge" class="dash-rise" style="animation-delay: 120ms">
      <Card class="border border-border/40 rounded-xl">
        <CardHeader class="pb-3">
          <div class="flex items-start justify-between gap-3">
            <div class="min-w-0">
              <CardTitle class="flex items-center gap-2 text-base">
                <Library class="h-4 w-4 text-violet-500" />
                {{ t('dashboard.knowledge.title') }}
              </CardTitle>
              <CardDescription class="mt-1 text-xs">
                {{ t('dashboard.knowledge.subtitle', { count: knowledgeCounts?.codeSystems ?? 0 }) }}
                <span v-if="knowledgeSystems.length"> · {{ knowledgeSystems.join(' / ') }}</span>
              </CardDescription>
            </div>
            <Button variant="ghost" size="sm" class="shrink-0 gap-1" @click="router.push({ name: 'Knowledge' })">
              {{ t('dashboard.knowledge.open') }}
              <ArrowRight class="h-3 w-3" />
            </Button>
          </div>
        </CardHeader>
        <CardContent class="pt-0">
          <div v-if="knowledgeLoading" class="flex items-center justify-center py-10">
            <Loader2 class="h-6 w-6 animate-spin text-muted-foreground" />
          </div>
          <div v-else-if="knowledgeStats.length" class="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-3">
            <div
              v-for="item in knowledgeStats"
              :key="item.key"
              class="rounded-xl border border-border/40 bg-muted/30 p-3.5 transition-colors hover:bg-muted/60"
            >
              <div class="flex items-baseline justify-between gap-2">
                <span class="truncate text-xs text-muted-foreground">{{ item.label }}</span>
                <span class="shrink-0 text-lg font-bold tabular-nums">{{ (knowledgeDisplay[item.key] ?? 0).toLocaleString('zh-CN') }}</span>
              </div>
              <div class="mt-2 h-1.5 overflow-hidden rounded-full bg-muted">
                <div
                  class="dash-bar h-full rounded-full"
                  :class="item.color"
                  :style="{ width: chartsReady ? `${item.width}%` : '0%' }"
                />
              </div>
              <p v-if="item.hint" class="mt-1.5 text-[10px] text-muted-foreground/80">{{ item.hint }}</p>
            </div>
          </div>
          <p v-else class="py-6 text-center text-xs text-muted-foreground">{{ t('dashboard.knowledge.empty') }}</p>
        </CardContent>
      </Card>
    </div>

    <!-- 修改密码弹窗 -->
    <Dialog v-model:open="pwdOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle class="flex items-center gap-2">
            <KeyRound class="h-5 w-5 text-primary" />
            {{ t('common.changePassword') }}
          </DialogTitle>
          <DialogDescription>{{ currentUserDisplay }}</DialogDescription>
        </DialogHeader>
        <form class="space-y-4" @submit.prevent="handleChangePassword">
          <div class="space-y-2">
            <Label for="old-password">{{ t('common.oldPassword') }}</Label>
            <Input id="old-password" v-model="pwdForm.oldPassword" type="password" autocomplete="current-password" :placeholder="t('common.oldPassword')" required />
          </div>
          <div class="space-y-2">
            <Label for="new-password">{{ t('common.newPassword') }}</Label>
            <Input id="new-password" v-model="pwdForm.newPassword" type="password" autocomplete="new-password" :placeholder="t('common.newPassword')" required />
            <p v-if="pwdTooShort" class="text-xs text-destructive">{{ t('common.passwordTooShort') }}</p>
          </div>
          <div class="space-y-2">
            <Label for="confirm-password">{{ t('common.confirmNewPassword') }}</Label>
            <Input id="confirm-password" v-model="pwdForm.confirmNewPassword" type="password" autocomplete="new-password" :placeholder="t('common.confirmNewPassword')" required />
            <p v-if="pwdMismatch" class="text-xs text-destructive">{{ t('common.passwordMismatch') }}</p>
          </div>
          <DialogFooter>
            <DialogClose as-child><Button type="button" variant="outline" :disabled="pwdSaving">{{ t('common.cancel') }}</Button></DialogClose>
            <Button type="submit" :disabled="pwdSaving || !pwdFormValid">
              <Loader2 v-if="pwdSaving" class="h-4 w-4 mr-1.5 animate-spin" />
              <KeyRound v-else class="h-4 w-4 mr-1.5" />
              {{ t('common.save') }}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </div>
</template>

<style scoped>
@keyframes dash-rise {
  from {
    opacity: 0;
    transform: translateY(10px) scale(0.99);
  }
  to {
    opacity: 1;
    transform: translateY(0) scale(1);
  }
}

.dash-rise {
  animation: dash-rise 0.45s cubic-bezier(0.22, 1, 0.36, 1) backwards;
}

.dash-kpi {
  animation: dash-rise 0.45s cubic-bezier(0.22, 1, 0.36, 1) backwards;
  transition: transform 0.25s ease, box-shadow 0.25s ease, border-color 0.25s ease;
}

.dash-kpi:nth-child(2) {
  animation-delay: 50ms;
}

.dash-kpi:nth-child(3) {
  animation-delay: 100ms;
}

.dash-kpi:nth-child(4) {
  animation-delay: 150ms;
}

.dash-kpi:hover {
  transform: translateY(-3px);
  box-shadow: 0 12px 26px -16px rgba(15, 23, 42, 0.45);
  border-color: rgba(139, 92, 246, 0.35);
}

.dash-kpi-icon {
  transition: transform 0.25s ease;
}

.dash-kpi:hover .dash-kpi-icon {
  transform: scale(1.12) rotate(-5deg);
}

/* 悬浮时从卡片左侧扫过的高光 */
.dash-shine::after {
  content: '';
  position: absolute;
  inset: -40% -60%;
  background: linear-gradient(105deg, transparent 42%, rgba(255, 255, 255, 0.3) 50%, transparent 58%);
  transform: translateX(-70%);
  transition: transform 0.7s ease;
  pointer-events: none;
}

.dash-shine:hover::after {
  transform: translateX(70%);
}

.dash-bar {
  transition: width 0.85s cubic-bezier(0.22, 1, 0.36, 1), height 0.85s cubic-bezier(0.22, 1, 0.36, 1);
}

.dash-spark {
  stroke-dasharray: 400;
  transition: stroke-dashoffset 1.1s cubic-bezier(0.22, 1, 0.36, 1);
}

.dash-donut-seg {
  transition: stroke-dasharray 0.9s cubic-bezier(0.22, 1, 0.36, 1), stroke-dashoffset 0.9s cubic-bezier(0.22, 1, 0.36, 1);
}

.dash-dot {
  transition: transform 0.2s ease;
}

.dash-dot:hover {
  transform: scale(1.4);
}

@media (prefers-reduced-motion: reduce) {
  .dash-rise,
  .dash-kpi {
    animation: none;
  }

  .dash-shine::after {
    display: none;
  }

  .dash-bar,
  .dash-spark,
  .dash-donut-seg,
  .dash-dot,
  .dash-kpi,
  .dash-kpi-icon {
    transition: none;
  }
}
</style>
