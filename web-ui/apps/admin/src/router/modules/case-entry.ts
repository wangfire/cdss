import type { RouteRecordRaw } from 'vue-router'
import { FilePlus2 } from 'lucide-vue-next'

const caseEntryRoutes: RouteRecordRaw[] = [
  {
    path: '/case-entry',
    name: 'CaseEntry',
    component: () => import('@/views/CaseEntry.vue'),
    meta: {
      titleKey: 'menu.caseEntry',
      title: 'Case Entry',
      icon: FilePlus2,
      order: 3,
      keepAlive: true,
    },
  },
]

export default caseEntryRoutes
