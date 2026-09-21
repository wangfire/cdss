import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi } from '@/api'
import { ApiError } from '@/api/http'
import { STORAGE_KEYS } from '@/constants/common'
import type { UserDto } from '@/api/types'

/**
 * 当前用户信息（与后端 UserDto 对齐）
 */
type UserInfo = UserDto

export const useUserStore = defineStore('user', () => {
  const token = ref<string | null>(localStorage.getItem(STORAGE_KEYS.TOKEN))
  const user = ref<UserInfo | null>(null)
  const roles = ref<string[]>([])
  const permissions = ref<string[]>([])
  const isLoading = ref(false)

  const isAuthenticated = computed(() => token.value !== null)

  /**
   * 生成默认头像 URL（基于用户名）
   */
  const defaultAvatar = computed(() => {
    if (user.value?.avatar) return user.value.avatar
    const name = user.value?.userName || user.value?.displayName || 'Admin'
    return `https://api.dicebear.com/7.x/avataaars/svg?seed=${encodeURIComponent(name)}&backgroundColor=b6e3f4,c0aede,d1d4f9,ffd5dc,ffdfbf`
  })

  const displayName = computed(() => {
    return user.value?.displayName || user.value?.userName || 'User'
  })

  /**
   * 用户登录：调用后端 API，持久化 token、用户信息、角色与权限
   */
  async function login(userName: string, password: string, hospitalId: string) {
    isLoading.value = true
    try {
      const res = await authApi.login({ userName, password, hospitalId })
      token.value = res.token
      user.value = res.user
      roles.value = res.roles
      permissions.value = res.permissions
      localStorage.setItem(STORAGE_KEYS.TOKEN, res.token)
      // 记住 hospitalId，供后续请求的医院隔离上下文使用。
      localStorage.setItem(STORAGE_KEYS.HOSPITAL, hospitalId)
      return res
    }
    finally {
      isLoading.value = false
    }
  }

  /**
   * 从后端拉取当前用户信息（基于 token）
   */
  async function fetchProfile() {
    if (!token.value) return null
    isLoading.value = true
    try {
      const res = await authApi.me()
      user.value = res.user
      roles.value = res.roles
      permissions.value = res.permissions
      return res
    }
    catch (err) {
      // 401 时清理
      if (err instanceof ApiError && err.status === 401) {
        clear()
      }
      throw err
    }
    finally {
      isLoading.value = false
    }
  }

  /**
   * 退出登录
   */
  async function logout() {
    try {
      if (token.value) await authApi.logout()
    }
    catch {
      /* ignore */
    }
    finally {
      clear()
    }
  }

  function clear() {
    token.value = null
    user.value = null
    roles.value = []
    permissions.value = []
    localStorage.removeItem(STORAGE_KEYS.TOKEN)
  }

  /**
   * 手动设置权限（如果需要从外部覆盖）
   */
  function setPermissions(perms: string[]) {
    permissions.value = perms
  }

  /**
   * 判断是否拥有指定权限（任一命中）
   */
  function hasAnyPermission(required?: string[]): boolean {
    if (!required || required.length === 0) return true
    return required.some(p => permissions.value.includes(p))
  }

  /**
   * 判断是否拥有全部权限
   */
  function hasAllPermissions(required?: string[]): boolean {
    if (!required || required.length === 0) return true
    return required.every(p => permissions.value.includes(p))
  }

  return {
    token,
    user,
    roles,
    permissions,
    isLoading,
    isAuthenticated,
    defaultAvatar,
    displayName,
    login,
    logout,
    fetchProfile,
    clear,
    setPermissions,
    hasAnyPermission,
    hasAllPermissions,
  }
})
