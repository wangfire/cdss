import type { RouteRecordRaw } from 'vue-router'
import { ClipboardList } from 'lucide-vue-next'

const workbenchRoutes: RouteRecordRaw[] = [
  {
    path: '/workbench',
    name: 'Workbench',
    component: () => import('@/views/Workbench.vue'),
    meta: {
      titleKey: 'menu.workbench',
      title: 'Workbench',
      icon: ClipboardList,
      order: 2,
      keepAlive: true,
    },
  },
]

export default workbenchRoutes
