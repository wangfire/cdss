<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Loader2, Plus, RefreshCw, ShieldCheck, UserPlus, X } from 'lucide-vue-next'
import { useUserStore } from '@/stores/user'
import { STORAGE_KEYS } from '@/constants/common'
import {
  ACCOUNT_MANAGE_PERMISSIONS,
  ALL_PERMISSION_CODES,
  PERMISSION_SUMMARY_GROUPS,
  PERMISSION_TREE,
  collectCodes,
  type PermissionNode,
} from '@/constants/permissions'
import { ApiError, authApi, securityApi } from '@/api'
import type { AppRoleDto, AppUserSummaryDto } from '@/api'
import PermissionTree from '@/components/PermissionTree.vue'
import {
  Badge,
  Button,
  Card,
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
  DropdownMenuTrigger,
  Input,
  Label,
  toast,
} from '@tabtab/ui'

/** Card 外壳是 py-6/gap-6，这里改成无内边距、由标题栏与内容区各自补 padding。 */
const SHELL = 'gap-0 py-0'
const HEAD = 'flex flex-wrap items-center gap-2 px-6 pt-5 pb-3'
const BODY = 'px-6 pb-5'

const { t } = useI18n()
const userStore = useUserStore()

const canManageUsers = computed(
  () => userStore.roles.includes('ADMIN')
    || userStore.hasAnyPermission(ACCOUNT_MANAGE_PERMISSIONS),
)
const hospitalId = computed(() => localStorage.getItem(STORAGE_KEYS.HOSPITAL) || '')

/** 角色卡只展示分组计数，完整清单在权限树里查看。 */
function rolePermissionSummary(role: AppRoleDto) {
  return PERMISSION_SUMMARY_GROUPS
    .map(group => {
      const count = group.codes.filter(code => role.permissions.includes(code)).length
      return { ...group, count, total: group.codes.length }
    })
    .filter(group => group.count > 0)
}

function fmt(value?: string | null) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : ''
}

/* ============== 我的账户 ============== */
const profileLoading = ref(false)
async function loadProfile() {
  if (profileLoading.value) return
  profileLoading.value = true
  try {
    await userStore.fetchProfile()
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.error'))
  }
  finally {
    profileLoading.value = false
  }
}

/* ============== 修改密码 ============== */
const pwdOpen = ref(false)
const pwdSaving = ref(false)
const pwdForm = ref({ oldPassword: '', newPassword: '', confirmNewPassword: '' })

// 后端 ChangePasswordAsync 要求新密码 ≥8 位，前端必须同口径。
const pwdTooShort = computed(
  () => pwdForm.value.newPassword.length > 0 && pwdForm.value.newPassword.length < 8,
)
const pwdMismatch = computed(
  () => pwdForm.value.confirmNewPassword.length > 0
    && pwdForm.value.newPassword !== pwdForm.value.confirmNewPassword,
)
const pwdFormValid = computed(
  () => pwdForm.value.oldPassword.length > 0
    && pwdForm.value.newPassword.length >= 8
    && pwdForm.value.newPassword === pwdForm.value.confirmNewPassword,
)

function openChangePassword() {
  pwdForm.value = { oldPassword: '', newPassword: '', confirmNewPassword: '' }
  pwdOpen.value = true
}

async function handleChangePassword() {
  if (pwdSaving.value || !pwdFormValid.value) return
  pwdSaving.value = true
  try {
    await authApi.changePassword({
      oldPassword: pwdForm.value.oldPassword,
      newPassword: pwdForm.value.newPassword,
    })
    pwdOpen.value = false
    toast.success(t('account.passwordChanged'))
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.changePasswordFailed'))
  }
  finally {
    pwdSaving.value = false
  }
}

/* ============== 用户与角色 ============== */
const users = ref<AppUserSummaryDto[]>([])
const roles = ref<AppRoleDto[]>([])
const directoryLoading = ref(false)

const roleNameByCode = computed(() => {
  const map: Record<string, string> = {}
  for (const role of roles.value) map[role.code] = role.name
  return map
})

async function loadDirectory() {
  if (!canManageUsers.value || directoryLoading.value) return
  directoryLoading.value = true
  try {
    const [userList, roleList] = await Promise.all([
      securityApi.listUsers(),
      securityApi.listRoles(),
    ])
    users.value = userList
    roles.value = roleList
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.error'))
  }
  finally {
    directoryLoading.value = false
  }
}

/** 一次只有一个行内动作在飞，pendingKey 用于给对应控件转圈。 */
const pendingKey = ref<string | null>(null)
async function runAction(key: string, action: () => Promise<void>, done: () => void) {
  if (pendingKey.value) return
  pendingKey.value = key
  try {
    await action()
    done()
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.error'))
  }
  finally {
    pendingKey.value = null
  }
}

function candidates(user: AppUserSummaryDto) {
  return roles.value.filter((role) => !user.roles.includes(role.code))
}

function assignRole(user: AppUserSummaryDto, role: AppRoleDto) {
  return runAction(`assign:${user.id}:${role.id}`, async () => {
    await securityApi.assignRole(user.id, role.id)
    await loadDirectory()
  }, () => toast.success(t('account.roleAssigned', { name: user.displayName, role: role.name })))
}

function removeRole(user: AppUserSummaryDto, roleCode: string) {
  const role = roles.value.find((item) => item.code === roleCode)
  if (!role) return
  return runAction(`remove:${user.id}:${role.id}`, async () => {
    await securityApi.removeRole(user.id, role.id)
    await loadDirectory()
  }, () => toast.success(t('account.roleRemoved', { name: user.displayName, role: role.name })))
}

function toggleStatus(user: AppUserSummaryDto) {
  const next = user.status === 'ACTIVE' ? 'DISABLED' : 'ACTIVE'
  return runAction(`status:${user.id}`, async () => {
    await securityApi.setUserStatus(user.id, next)
    await loadDirectory()
  }, () => toast.success(
    next === 'ACTIVE'
      ? t('account.enabledAt', { code: user.code })
      : t('account.disabledAt', { code: user.code }),
  ))
}

/* 新建用户 */
const userDialogOpen = ref(false)
const userSaving = ref(false)
const userForm = ref({ code: '', displayName: '' })
const userFormValid = computed(
  () => userForm.value.code.trim().length > 0 && userForm.value.displayName.trim().length > 0,
)

function openUserDialog() {
  userForm.value = { code: '', displayName: '' }
  userDialogOpen.value = true
}

async function handleCreateUser() {
  if (userSaving.value || !userFormValid.value) return
  userSaving.value = true
  const code = userForm.value.code.trim()
  try {
    await securityApi.createUser({ code, displayName: userForm.value.displayName.trim() })
    userDialogOpen.value = false
    toast.success(t('account.userCreated', { code }))
    await loadDirectory()
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.error'))
  }
  finally {
    userSaving.value = false
  }
}

/* 新建角色 */
const roleDialogOpen = ref(false)
const roleSaving = ref(false)
const roleForm = ref({ code: '', name: '', description: '' })
const roleFormValid = computed(
  () => roleForm.value.code.trim().length > 0 && roleForm.value.name.trim().length > 0,
)

function openRoleDialog() {
  roleForm.value = { code: '', name: '', description: '' }
  roleDialogOpen.value = true
}

async function handleCreateRole() {
  if (roleSaving.value || !roleFormValid.value) return
  roleSaving.value = true
  const code = roleForm.value.code.trim()
  try {
    await securityApi.createRole({
      code,
      name: roleForm.value.name.trim(),
      description: roleForm.value.description.trim() || null,
    })
    roleDialogOpen.value = false
    toast.success(t('account.roleCreated', { code }))
    await loadDirectory()
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.error'))
  }
  finally {
    roleSaving.value = false
  }
}

/* 配置角色权限 */
const permDialogOpen = ref(false)
const permSaving = ref(false)
const permRole = ref<AppRoleDto | null>(null)
const permSelected = ref<string[]>([])

const permIsAdmin = computed(() => permRole.value?.code.toUpperCase() === 'ADMIN')

function openPermissionDialog(role: AppRoleDto) {
  permRole.value = role
  permSelected.value = [...role.permissions]
  permDialogOpen.value = true
}

const permSelectedCount = computed(
  () => ALL_PERMISSION_CODES.filter(code => permSelected.value.includes(code)).length,
)

function handlePermissionToggle(payload: {
  node: PermissionNode
  checked: boolean
  ancestors: string[]
}) {
  const codes = collectCodes(payload.node)
  if (payload.checked) {
    // 选中按钮时一并带上祖先功能码：没有页面入口的按钮权限不可用。
    const next = new Set(permSelected.value)
    for (const code of [...payload.ancestors, ...codes]) next.add(code)
    permSelected.value = [...next]
    return
  }
  const removed = new Set(codes)
  permSelected.value = permSelected.value.filter(code => !removed.has(code))
}

function selectAllPermissions() {
  permSelected.value = [...ALL_PERMISSION_CODES]
}

function clearPermissions() {
  permSelected.value = []
}

async function handleSavePermissions() {
  if (permSaving.value || !permRole.value || permIsAdmin.value) return
  permSaving.value = true
  const role = permRole.value
  try {
    await securityApi.updateRolePermissions(role.id, permSelected.value)
    permDialogOpen.value = false
    toast.success(t('account.permissionsSaved', { role: role.name }))
    await loadDirectory()
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('common.error'))
  }
  finally {
    permSaving.value = false
  }
}

onMounted(async () => {
  await Promise.all([loadProfile(), loadDirectory()])
})
</script>

<template>
  <div class="space-y-4">
    <!-- 我的账户 -->
    <Card :class="SHELL">
      <div :class="HEAD">
        <h2 class="text-sm font-semibold">{{ t('account.myAccount') }}</h2>
        <span class="text-xs text-muted-foreground">{{ t('account.myAccountDesc') }}</span>
        <div class="ml-auto flex items-center gap-2">
          <Button variant="outline" size="sm" class="gap-1" :disabled="profileLoading" @click="loadProfile">
            <RefreshCw class="size-4" :class="{ 'animate-spin': profileLoading }" />
            <span class="hidden md:inline">{{ t('account.refresh') }}</span>
          </Button>
          <Button size="sm" @click="openChangePassword">
            {{ t('common.changePassword') }}
          </Button>
        </div>
      </div>
      <div :class="BODY">
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <div class="rounded-lg bg-muted/50 p-3">
            <p class="text-xs text-muted-foreground">{{ t('account.username') }}</p>
            <p class="mt-1 truncate text-sm font-medium">{{ userStore.user?.userName || '-' }}</p>
          </div>
          <div class="rounded-lg bg-muted/50 p-3">
            <p class="text-xs text-muted-foreground">{{ t('account.displayName') }}</p>
            <p class="mt-1 truncate text-sm font-medium">{{ userStore.displayName }}</p>
          </div>
          <div class="rounded-lg bg-muted/50 p-3">
            <p class="text-xs text-muted-foreground">{{ t('account.lastLogin') }}</p>
            <p class="mt-1 truncate text-sm font-medium">
              {{ fmt(userStore.user?.lastLoginAt) || t('account.never') }}
            </p>
          </div>
          <div class="rounded-lg bg-muted/50 p-3">
            <p class="text-xs text-muted-foreground">{{ t('account.roles') }}</p>
            <div v-if="userStore.roles.length" class="mt-1.5 flex flex-wrap gap-1">
              <Badge
                v-for="code in userStore.roles"
                :key="code"
                variant="secondary"
                class="px-1.5 py-0 text-[11px]"
              >
                {{ roleNameByCode[code] || code }}
              </Badge>
            </div>
            <p v-else class="mt-1 text-sm text-muted-foreground">{{ t('account.noRoles') }}</p>
          </div>
        </div>
        <p v-if="hospitalId" class="mt-3 truncate text-xs text-muted-foreground" :title="hospitalId">
          {{ t('account.hospitalContext') }}: {{ hospitalId }}
        </p>
      </div>
    </Card>

    <!-- 无用户管理权限：只读自己的账户 -->
    <Card v-if="!canManageUsers" :class="SHELL">
      <div :class="HEAD">
        <ShieldCheck class="size-4 text-muted-foreground" />
        <h2 class="text-sm font-semibold">{{ t('account.users') }}</h2>
      </div>
      <div :class="BODY">
        <p class="text-sm text-muted-foreground">{{ t('account.adminOnly') }}</p>
      </div>
    </Card>

    <!-- 用户管理 + 角色管理 -->
    <div v-else class="grid grid-cols-1 gap-4 xl:grid-cols-3">
      <Card class="xl:col-span-2" :class="SHELL">
        <div :class="HEAD">
          <h2 class="text-sm font-semibold">{{ t('account.users') }}</h2>
          <span class="text-xs text-muted-foreground">{{ t('account.usersDesc') }}</span>
          <div class="ml-auto flex items-center gap-2">
            <Button variant="outline" size="sm" class="gap-1" :disabled="directoryLoading" @click="loadDirectory">
              <RefreshCw class="size-4" :class="{ 'animate-spin': directoryLoading }" />
              <span class="hidden md:inline">{{ t('account.refresh') }}</span>
            </Button>
            <Button size="sm" class="gap-1" @click="openUserDialog">
              <UserPlus class="size-4" />
              {{ t('account.newUser') }}
            </Button>
          </div>
        </div>
        <div :class="BODY">
          <div v-if="directoryLoading && users.length === 0" class="flex justify-center py-10">
            <Loader2 class="size-6 animate-spin text-muted-foreground" />
          </div>
          <p v-else-if="users.length === 0" class="py-6 text-center text-sm text-muted-foreground">
            {{ t('account.emptyUsers') }}
          </p>
          <div v-else class="divide-y divide-border/60">
            <div
              v-for="user in users"
              :key="user.id"
              class="flex flex-wrap items-center gap-3 py-2.5"
            >
              <div class="w-32 shrink-0">
                <p class="truncate text-sm font-medium" :title="user.code">{{ user.code }}</p>
                <p class="truncate text-xs text-muted-foreground">{{ user.displayName }}</p>
              </div>
              <div class="w-24 shrink-0">
                <Badge
                  variant="outline"
                  class="text-[11px]"
                  :class="user.status === 'ACTIVE'
                    ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600'
                    : 'border-red-500/30 bg-red-500/10 text-red-600'"
                >
                  {{ user.status === 'ACTIVE' ? t('account.statusActive') : t('account.statusDisabled') }}
                </Badge>
                <p class="mt-0.5 truncate text-[11px] text-muted-foreground" :title="fmt(user.lastLoginAt) || undefined">
                  {{ fmt(user.lastLoginAt) || t('account.never') }}
                </p>
              </div>
              <div class="flex min-w-[14rem] flex-1 flex-wrap items-center gap-1.5">
                <span
                  v-for="code in user.roles"
                  :key="code"
                  class="inline-flex items-center gap-1 rounded-md bg-muted px-1.5 py-0.5 text-[11px]"
                >
                  {{ roleNameByCode[code] || code }}
                  <button
                    type="button"
                    class="cursor-pointer text-muted-foreground hover:text-destructive disabled:cursor-not-allowed"
                    :title="t('account.removeRole')"
                    :disabled="!!pendingKey"
                    @click="removeRole(user, code)"
                  >
                    <X class="size-3" />
                  </button>
                </span>
                <span v-if="user.roles.length === 0" class="text-[11px] text-muted-foreground">
                  {{ t('account.noRoles') }}
                </span>
                <DropdownMenu v-if="candidates(user).length">
                  <DropdownMenuTrigger as-child>
                    <Button
                      variant="outline"
                      size="sm"
                      class="h-6 gap-1 px-1.5 text-[11px]"
                      :disabled="!!pendingKey"
                    >
                      <Plus class="size-3" />
                      {{ t('account.assignRole') }}
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="start" class="w-48">
                    <DropdownMenuItem
                      v-for="role in candidates(user)"
                      :key="role.id"
                      class="cursor-pointer"
                      @select="assignRole(user, role)"
                    >
                      <span class="truncate">{{ role.name }}</span>
                      <span class="ml-auto truncate text-[11px] text-muted-foreground">{{ role.code }}</span>
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </div>
              <Button
                variant="ghost"
                size="sm"
                class="shrink-0 gap-1 text-xs"
                :disabled="!!pendingKey"
                @click="toggleStatus(user)"
              >
                <Loader2 v-if="pendingKey === `status:${user.id}`" class="size-3 animate-spin" />
                {{ user.status === 'ACTIVE' ? t('account.disable') : t('account.enable') }}
              </Button>
            </div>
          </div>
        </div>
      </Card>

      <Card :class="SHELL">
        <div :class="HEAD">
          <h2 class="text-sm font-semibold">{{ t('account.rolesTitle') }}</h2>
          <Button
            class="ml-auto gap-1"
            variant="outline"
            size="sm"
            @click="openRoleDialog"
          >
            <Plus class="size-4" />
            {{ t('account.newRole') }}
          </Button>
        </div>
        <div :class="BODY">
          <p v-if="roles.length === 0" class="py-6 text-center text-sm text-muted-foreground">
            {{ t('account.noRoles') }}
          </p>
          <div v-else class="divide-y divide-border/60">
            <div v-for="role in roles" :key="role.id" class="py-2.5">
              <div class="flex flex-wrap items-center gap-2">
                <span class="truncate text-sm font-medium">{{ role.name }}</span>
                <code class="shrink-0 rounded bg-muted px-1.5 py-0.5 text-[11px]">{{ role.code }}</code>
                <Button
                  variant="outline"
                  size="sm"
                  class="ml-auto h-6 shrink-0 gap-1 px-1.5 text-[11px]"
                  @click="openPermissionDialog(role)"
                >
                  <ShieldCheck class="size-3" />
                  {{ role.code.toUpperCase() === 'ADMIN'
                    ? t('account.allPermissions')
                    : t('account.configurePermissions') }}
                </Button>
              </div>
              <p v-if="role.description" class="mt-1 truncate text-xs text-muted-foreground">
                {{ role.description }}
              </p>
              <div
                v-if="role.code.toUpperCase() === 'ADMIN'"
                class="mt-1.5 flex flex-wrap gap-1"
              >
                <Badge variant="secondary" class="px-1.5 py-0 text-[11px]">
                  {{ t('account.allPermissions') }}
                </Badge>
              </div>
              <div
                v-else-if="role.permissions.length"
                class="mt-1.5 flex flex-wrap gap-1"
              >
                <Badge
                  v-for="group in rolePermissionSummary(role)"
                  :key="group.key"
                  variant="secondary"
                  class="px-1.5 py-0 text-[11px]"
                >
                  {{ t('account.roleSummaryItem', {
                    label: t(group.labelKey),
                    count: group.count,
                    total: group.total,
                  }) }}
                </Badge>
              </div>
              <p v-else class="mt-1.5 text-[11px] text-muted-foreground">
                {{ t('account.noPermissions') }}
              </p>
            </div>
          </div>
        </div>
      </Card>
    </div>

    <!-- 新建用户 -->
    <Dialog v-model:open="userDialogOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle class="flex items-center gap-2">
            <UserPlus class="size-5 text-primary" />
            {{ t('account.newUserTitle') }}
          </DialogTitle>
          <DialogDescription>{{ t('account.newUserHint') }}</DialogDescription>
        </DialogHeader>
        <form class="space-y-4" @submit.prevent="handleCreateUser">
          <div class="space-y-2">
            <Label for="new-user-code">{{ t('account.account') }}</Label>
            <Input id="new-user-code" v-model="userForm.code" placeholder="op-001" required />
          </div>
          <div class="space-y-2">
            <Label for="new-user-name">{{ t('account.fullName') }}</Label>
            <Input id="new-user-name" v-model="userForm.displayName" required />
          </div>
          <DialogFooter>
            <DialogClose as-child>
              <Button type="button" variant="outline" :disabled="userSaving">{{ t('common.cancel') }}</Button>
            </DialogClose>
            <Button type="submit" :disabled="userSaving || !userFormValid">
              <Loader2 v-if="userSaving" class="mr-1.5 size-4 animate-spin" />
              {{ t('common.save') }}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <!-- 新建角色 -->
    <Dialog v-model:open="roleDialogOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle class="flex items-center gap-2">
            <ShieldCheck class="size-5 text-primary" />
            {{ t('account.newRole') }}
          </DialogTitle>
          <DialogDescription>{{ t('account.rolesDesc') }}</DialogDescription>
        </DialogHeader>
        <form class="space-y-4" @submit.prevent="handleCreateRole">
          <div class="space-y-2">
            <Label for="new-role-code">{{ t('account.roleCode') }}</Label>
            <Input id="new-role-code" v-model="roleForm.code" placeholder="ROLE_CODER" required />
          </div>
          <div class="space-y-2">
            <Label for="new-role-name">{{ t('account.roleName') }}</Label>
            <Input id="new-role-name" v-model="roleForm.name" required />
          </div>
          <div class="space-y-2">
            <Label for="new-role-desc">{{ t('account.roleDescription') }}</Label>
            <Input id="new-role-desc" v-model="roleForm.description" />
          </div>
          <DialogFooter>
            <DialogClose as-child>
              <Button type="button" variant="outline" :disabled="roleSaving">{{ t('common.cancel') }}</Button>
            </DialogClose>
            <Button type="submit" :disabled="roleSaving || !roleFormValid">
              <Loader2 v-if="roleSaving" class="mr-1.5 size-4 animate-spin" />
              {{ t('common.save') }}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <!-- 配置角色权限 -->
    <Dialog v-model:open="permDialogOpen">
      <DialogContent class="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle class="flex items-center gap-2">
            <ShieldCheck class="size-5 text-primary" />
            {{ t('account.configurePermissions') }} · {{ permRole?.name }}
          </DialogTitle>
          <DialogDescription>{{ t('account.permissionsHint') }}</DialogDescription>
        </DialogHeader>
        <div class="max-h-[65vh] space-y-3 overflow-y-auto pr-1">
          <div
            v-if="permIsAdmin"
            class="rounded-md border border-amber-500/30 bg-amber-500/10 p-3 text-xs text-amber-700"
          >
            {{ t('account.adminPermissionsFixed') }}
          </div>
          <div
            v-else
            class="flex flex-wrap items-center justify-between gap-2 rounded-md bg-muted/50 px-3 py-2 text-xs text-muted-foreground"
          >
            <span>
              {{ t('account.permissionSelection', {
                count: permSelectedCount,
                total: ALL_PERMISSION_CODES.length,
              }) }}
            </span>
            <span class="flex items-center gap-3">
              <button
                type="button"
                class="cursor-pointer text-primary hover:underline"
                @click="selectAllPermissions"
              >
                {{ t('account.selectAll') }}
              </button>
              <button
                type="button"
                class="cursor-pointer hover:underline"
                @click="clearPermissions"
              >
                {{ t('account.clearAll') }}
              </button>
            </span>
          </div>
          <p class="text-[11px] leading-4 text-muted-foreground">
            {{ t('account.permissionTreeHint') }}
          </p>
          <section
            v-for="menu in PERMISSION_TREE"
            :key="menu.key"
            class="rounded-lg border border-border/60 p-1"
            :class="{ 'opacity-70': permIsAdmin }"
          >
            <PermissionTree
              :node="menu"
              :selected="permSelected"
              :disabled="permIsAdmin"
              @toggle="handlePermissionToggle"
            />
          </section>
        </div>
        <DialogFooter>
          <DialogClose as-child>
            <Button type="button" variant="outline" :disabled="permSaving">
              {{ t('common.cancel') }}
            </Button>
          </DialogClose>
          <Button type="button" :disabled="permSaving || permIsAdmin" @click="handleSavePermissions">
            <Loader2 v-if="permSaving" class="mr-1.5 size-4 animate-spin" />
            {{ t('common.save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- 修改密码 -->
    <Dialog v-model:open="pwdOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{{ t('common.changePassword') }}</DialogTitle>
          <DialogDescription>{{ t('account.changePasswordHint') }}</DialogDescription>
        </DialogHeader>
        <form class="space-y-4" @submit.prevent="handleChangePassword">
          <div class="space-y-2">
            <Label for="account-old-password">{{ t('common.oldPassword') }}</Label>
            <Input id="account-old-password" v-model="pwdForm.oldPassword" type="password" autocomplete="current-password" required />
          </div>
          <div class="space-y-2">
            <Label for="account-new-password">{{ t('common.newPassword') }}</Label>
            <Input id="account-new-password" v-model="pwdForm.newPassword" type="password" autocomplete="new-password" required />
            <p v-if="pwdTooShort" class="text-xs text-destructive">{{ t('common.passwordTooShort') }}</p>
          </div>
          <div class="space-y-2">
            <Label for="account-confirm-password">{{ t('common.confirmNewPassword') }}</Label>
            <Input id="account-confirm-password" v-model="pwdForm.confirmNewPassword" type="password" autocomplete="new-password" required />
            <p v-if="pwdMismatch" class="text-xs text-destructive">{{ t('common.passwordMismatch') }}</p>
          </div>
          <DialogFooter>
            <DialogClose as-child>
              <Button type="button" variant="outline" :disabled="pwdSaving">{{ t('common.cancel') }}</Button>
            </DialogClose>
            <Button type="submit" :disabled="pwdSaving || !pwdFormValid">
              <Loader2 v-if="pwdSaving" class="mr-1.5 size-4 animate-spin" />
              {{ t('common.save') }}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </div>
</template>
