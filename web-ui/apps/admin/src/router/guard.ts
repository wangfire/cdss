import type { Router } from 'vue-router'
import { useUserStore } from '@/stores/user'
import { i18n } from '@/i18n'
import { STORAGE_KEYS } from '@/constants/common'

const t = i18n.global.t

/**
 * 设置路由守卫
 * @param router - Vue Router 实例
 */
export function setupRouterGuard(router: Router) {
  router.beforeEach(async (to) => {
    const userStore = useUserStore()
    const token = localStorage.getItem(STORAGE_KEYS.TOKEN)

    // 1. 未登录：去 Login
    if (to.meta.requiresAuth !== false && !token) {
      return {
        name: 'Login',
        query: { redirect: to.fullPath },
      }
    }

    // 2. 已登录：但 user store 还没有信息（首次进入），拉取当前用户
    if (token && !userStore.user) {
      try {
        await userStore.fetchProfile()
      }
      catch {
        userStore.clear()
        return {
          name: 'Login',
          query: { redirect: to.fullPath },
        }
      }
    }

    // 3. 已登录但访问 Login：跳到 Dashboard
    if (to.name === 'Login' && token) {
      return { name: 'Dashboard' }
    }

    // 4. 权限校验
    if (to.meta.permissions?.length) {
      const userPermissions = userStore.permissions || []
      const hasPermission = to.meta.permissions.some((permission) =>
        userPermissions.includes(permission),
      )
      if (!hasPermission) {
        return { name: '403' }
      }
    }

    // 5. 标题
    const titleKey = to.meta.titleKey as string
    const title = titleKey ? t(titleKey) : (to.meta.title as string)
    document.title = title ? `${title} | TabTab Admin` : 'TabTab Admin'
  })

  router.afterEach(() => {
    window.scrollTo(0, 0)
  })
}
