import type { RouteRecordRaw } from 'vue-router'
import { LayoutDashboard } from 'lucide-vue-next'

/**
 * 仪表盘路由模块
 */
const dashboardRoutes: RouteRecordRaw[] = [
  {
    path: '/dashboard',
    name: 'Dashboard',
    component: () => import('@/views/Dashboard.vue'),
    meta: {
      titleKey: 'menu.dashboard',
      title: 'Dashboard',
      icon: LayoutDashboard,
      order: 1,
      keepAlive: true,
    },
  },
]

export default dashboardRoutes
