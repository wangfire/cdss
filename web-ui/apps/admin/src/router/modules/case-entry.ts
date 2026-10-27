import type { RouteRecordRaw } from 'vue-router'
import { FilePlus2 } from 'lucide-vue-next'
import { CASE_ENTRY_PERMISSIONS } from '@/constants/permissions'

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
      permissions: CASE_ENTRY_PERMISSIONS,
    },
  },
]

export default caseEntryRoutes
