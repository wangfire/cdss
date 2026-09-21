<script setup lang="ts">
import {
  Activity,
  AlertCircle,
  ArrowRight,
  BookOpen,
  ClipboardList,
  FileText,
  KeyRound,
  Loader2,
  LogOut,
  Server,
} from 'lucide-vue-next'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useUserStore } from '@/stores/user'
import { ApiError, authApi, healthApi, workbenchApi } from '@/api'
import type { CodingTaskStatus, WorkbenchTaskResponse } from '@/api'
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
  ScrollArea,
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
const pwdTooShort = computed(() => pwdForm.value.newPassword.length > 0 && pwdForm.value.newPassword.length < 6)
const pwdFormValid = computed(
  () => pwdForm.value.oldPassword.length > 0 && pwdForm.value.newPassword.length >= 6 && pwdForm.value.newPassword === pwdForm.value.confirmNewPassword,
)

async function handleChangePassword() {
  if (pwdSaving.value) return
  if (!pwdForm.value.oldPassword) { toast.warning(t('common.oldPassword')); return }
  if (pwdForm.value.newPassword.length < 6) { toast.warning(t('common.passwordTooShort')); return }
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
const serviceChecks = ref<Record<string, { status: string; latencyMs?: number }>>({})
const tasks = ref<WorkbenchTaskResponse[]>([])
const tasksLoading = ref(true)

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

const recentTasks = computed(() =>
  [...tasks.value].sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()).slice(0, 8),
)

const statusLabel = (status: CodingTaskStatus) => {
  const map: Record<string, string> = {
    PENDING: t('workbench.statusPending'),
    RUNNING: t('workbench.statusRunning'),
    SUCCESS: t('workbench.statusSuccess'),
    FAILED: t('workbench.statusFailed'),
    RETRYING: t('workbench.statusRetrying'),
    TIMEOUT: t('workbench.statusTimeout'),
    CANCELLED: t('workbench.statusCancelled'),
    HUMAN_REQUIRED: t('workbench.statusHumanRequired'),
    PENDING_REVIEW: t('workbench.statusPendingReview'),
    ACCEPTED: t('workbench.statusAccepted'),
    MODIFIED: t('workbench.statusModified'),
    REJECTED: t('workbench.statusRejected'),
  }
  return map[status] || status
}

const statusColor = (status: CodingTaskStatus) => {
  const map: Record<string, string> = {
    PENDING: 'bg-slate-500/10 text-slate-600',
    RUNNING: 'bg-blue-500/10 text-blue-600',
    SUCCESS: 'bg-emerald-500/10 text-emerald-600',
    FAILED: 'bg-red-500/10 text-red-600',
    RETRYING: 'bg-amber-500/10 text-amber-600',
    TIMEOUT: 'bg-orange-500/10 text-orange-600',
    CANCELLED: 'bg-slate-500/10 text-slate-600',
    HUMAN_REQUIRED: 'bg-violet-500/10 text-violet-600',
    PENDING_REVIEW: 'bg-violet-500/10 text-violet-600',
    ACCEPTED: 'bg-emerald-500/10 text-emerald-600',
    MODIFIED: 'bg-blue-500/10 text-blue-600',
    REJECTED: 'bg-red-500/10 text-red-600',
  }
  return map[status] || 'bg-slate-500/10 text-slate-600'
}

async function loadData() {
  loading.value = true
  tasksLoading.value = true
  try {
    const [health, taskList] = await Promise.allSettled([
      healthApi.readiness(),
      workbenchApi.listTasks(),
    ])
    if (health.status === 'fulfilled') {
      healthStatus.value = health.value.status
      serviceChecks.value = health.value.checks || {}
    }
    if (taskList.status === 'fulfilled') {
      tasks.value = taskList.value
    }
  }
  finally {
    loading.value = false
    tasksLoading.value = false
  }
}

onMounted(loadData)

const quickActions = [
  { id: '1', label: t('workbench.title'), icon: ClipboardList, onClick: () => router.push({ name: 'Workbench' }), variant: 'primary' as const },
  { id: '2', label: t('knowledge.title'), icon: BookOpen, onClick: () => router.push({ name: 'Knowledge' }), variant: 'default' as const },
]
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
    <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
      <Card class="group relative overflow-hidden transition-all duration-300 hover:-translate-y-1">
        <CardContent class="relative p-5">
          <div class="flex items-start justify-between">
            <div class="flex-1 min-w-0">
              <div class="flex items-center gap-2 mb-3">
                <div class="p-2 rounded-lg bg-violet-500/10">
                  <ClipboardList class="h-4 w-4 text-violet-500" />
                </div>
                <span class="text-sm font-medium text-muted-foreground">{{ t('workbench.title') }}</span>
              </div>
              <div class="text-2xl font-bold tracking-tight mb-1">{{ tasks.length }}</div>
              <p class="text-xs text-muted-foreground truncate">{{ t('dashboard.metrics.totalTasks') }}</p>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card class="group relative overflow-hidden transition-all duration-300 hover:-translate-y-1">
        <CardContent class="relative p-5">
          <div class="flex items-start justify-between">
            <div class="flex-1 min-w-0">
              <div class="flex items-center gap-2 mb-3">
                <div class="p-2 rounded-lg bg-amber-500/10">
                  <AlertCircle class="h-4 w-4 text-amber-500" />
                </div>
                <span class="text-sm font-medium text-muted-foreground">{{ t('dashboard.pendingReview') }}</span>
              </div>
              <div class="text-2xl font-bold tracking-tight mb-1">{{ pendingReviewCount }}</div>
              <p class="text-xs text-muted-foreground truncate">{{ t('workbench.statusHumanRequired') }} / {{ t('workbench.statusPendingReview') }}</p>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card class="group relative overflow-hidden transition-all duration-300 hover:-translate-y-1">
        <CardContent class="relative p-5">
          <div class="flex items-start justify-between">
            <div class="flex-1 min-w-0">
              <div class="flex items-center gap-2 mb-3">
                <div class="p-2 rounded-lg bg-emerald-500/10">
                  <Activity class="h-4 w-4 text-emerald-500" />
                </div>
                <span class="text-sm font-medium text-muted-foreground">{{ t('dashboard.completed') }}</span>
              </div>
              <div class="text-2xl font-bold tracking-tight mb-1">{{ acceptedCount }}</div>
              <p class="text-xs text-muted-foreground truncate">{{ t('workbench.statusAccepted') }} / {{ t('workbench.statusModified') }}</p>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card class="group relative overflow-hidden transition-all duration-300 hover:-translate-y-1">
        <CardContent class="relative p-5">
          <div class="flex items-start justify-between">
            <div class="flex-1 min-w-0">
              <div class="flex items-center gap-2 mb-3">
                <div class="p-2 rounded-lg bg-blue-500/10">
                  <Server class="h-4 w-4 text-blue-500" />
                </div>
                <span class="text-sm font-medium text-muted-foreground">{{ t('dashboard.systemStatus') }}</span>
              </div>
              <div class="text-2xl font-bold tracking-tight mb-1">
                <Badge :variant="healthStatus === 'Healthy' ? 'default' : 'destructive'" class="text-sm">
                  {{ healthStatus === 'Healthy' ? t('dashboard.operational') : healthStatus }}
                </Badge>
              </div>
              <p class="text-xs text-muted-foreground truncate">
                {{ Object.keys(serviceChecks).length }} {{ t('dashboard.apiService') }}
              </p>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>

    <!-- 主要内容 -->
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
      <!-- 左侧：快捷操作 + 系统状态 -->
      <div class="space-y-6">
        <Card class="border border-border/40">
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

        <Card class="bg-muted/40 border border-border/50 rounded-xl">
          <CardHeader class="pb-4">
            <div class="flex items-center justify-between">
              <CardTitle class="text-base">{{ t('dashboard.systemStatus') }}</CardTitle>
              <Badge variant="outline" class="text-emerald-500 border-emerald-500/20 bg-emerald-500/10">
                <div class="w-1.5 h-1.5 bg-emerald-500 mr-1.5 animate-pulse rounded-full" />
                {{ healthStatus === 'Healthy' ? t('dashboard.operational') : healthStatus }}
              </Badge>
            </div>
          </CardHeader>
          <CardContent class="pt-0">
            <div class="space-y-3">
              <div v-for="(check, name) in serviceChecks" :key="name" class="flex items-center justify-between p-3 bg-muted/50 hover:bg-muted transition-colors rounded-lg">
                <div class="flex items-center gap-3">
                  <div class="w-2 h-2 rounded-full" :class="check.status === 'Healthy' ? 'bg-emerald-500' : 'bg-red-500'" />
                  <span class="text-sm font-medium">{{ name }}</span>
                </div>
                <span class="text-xs text-muted-foreground">{{ check.status }}</span>
              </div>
              <div v-if="Object.keys(serviceChecks).length === 0" class="text-sm text-muted-foreground text-center py-4">
                {{ t('common.loading') }}
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- 中间：最近任务 -->
      <div class="lg:col-span-2 space-y-6">
        <Card class="border border-border/40 rounded-xl">
          <CardHeader class="pb-3">
            <div class="flex items-center justify-between">
              <CardTitle class="text-base font-semibold">{{ t('dashboard.recentActivities') }}</CardTitle>
              <Button variant="ghost" size="sm" class="gap-1" @click="router.push({ name: 'Workbench' })">
                {{ t('dashboard.viewDetails') }}
                <ArrowRight class="h-3 w-3" />
              </Button>
            </div>
          </CardHeader>
          <CardContent class="pt-0">
            <ScrollArea class="h-[340px] pr-4">
              <div v-if="tasksLoading" class="flex items-center justify-center py-12">
                <Loader2 class="h-6 w-6 animate-spin text-muted-foreground" />
              </div>
              <div v-else-if="recentTasks.length === 0" class="text-center py-12 text-muted-foreground">
                <FileText class="h-10 w-10 mx-auto mb-3 opacity-40" />
                <p class="text-sm">{{ t('workbench.noTasks') }}</p>
                <p class="text-xs mt-1">{{ t('workbench.noTasksDesc') }}</p>
              </div>
              <div v-else class="space-y-3">
                <div
                  v-for="task in recentTasks"
                  :key="task.taskId"
                  class="group flex items-start gap-3 p-3 transition-all duration-200 hover:bg-muted/50 cursor-pointer rounded-lg"
                  @click="router.push({ name: 'Workbench', query: { task: task.taskId } })"
                >
                  <div class="w-2 h-2 mt-2 rounded-full flex-shrink-0" :class="statusColor(task.status).split(' ')[1]?.replace('text-', 'bg-') || 'bg-slate-500'" />
                  <div class="flex-1 min-w-0">
                    <div class="flex items-center justify-between gap-2">
                      <p class="text-sm font-medium truncate group-hover:text-primary transition-colors">
                        {{ task.taskId.slice(0, 8) }}...
                      </p>
                      <Badge class="text-xs flex-shrink-0" :class="statusColor(task.status)" variant="outline">
                        {{ statusLabel(task.status) }}
                      </Badge>
                    </div>
                    <p class="text-xs text-muted-foreground mt-1 truncate">
                      {{ t('workbench.recommendations') }}: {{ task.recommendationCount }}
                    </p>
                    <p class="text-xs text-muted-foreground/70 mt-1.5">
                      {{ new Date(task.createdAt).toLocaleString('zh-CN') }}
                    </p>
                  </div>
                </div>
              </div>
            </ScrollArea>
          </CardContent>
        </Card>

        <!-- 任务状态分布 -->
        <Card class="bg-muted/40 border border-border/50 rounded-xl">
          <CardHeader class="pb-4">
            <CardTitle class="text-base">{{ t('dashboard.orderStatus') }}</CardTitle>
          </CardHeader>
          <CardContent class="pt-0">
            <div v-if="tasks.length > 0" class="grid grid-cols-2 sm:grid-cols-4 gap-3">
              <div
                v-for="(count, status) in statusCounts"
                :key="status"
                class="text-center p-3 bg-muted/50 rounded-lg"
              >
                <p class="text-lg font-semibold">{{ count }}</p>
                <p class="text-xs text-muted-foreground">{{ statusLabel(status as CodingTaskStatus) }}</p>
              </div>
            </div>
            <div v-else class="text-center py-8 text-muted-foreground text-sm">
              {{ t('workbench.noTasks') }}
            </div>
          </CardContent>
        </Card>
      </div>
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
