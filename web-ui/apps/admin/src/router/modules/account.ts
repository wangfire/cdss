import type { RouteRecordRaw } from 'vue-router'
import { UserCog } from 'lucide-vue-next'

/**
 * 账户设置：从侧栏页脚 / 顶栏用户菜单进入，不进主菜单（hideInMenu）。
 * 非 ADMIN 也能进来看自己的账户与改密，因此不设 permissions 门槛；
 * 用户管理区在页面内按角色隐藏。
 */
const accountRoutes: RouteRecordRaw[] = [
  {
    path: '/account',
    name: 'Account',
    component: () => import('@/views/Account.vue'),
    meta: {
      titleKey: 'menu.account',
      title: 'Account',
      icon: UserCog,
      order: 90,
      hideInMenu: true,
      keepAlive: true,
    },
  },
]

export default accountRoutes
