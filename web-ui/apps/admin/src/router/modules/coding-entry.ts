import type { RouteRecordRaw } from 'vue-router'
import { ClipboardList } from 'lucide-vue-next'
import { CASE_ENTRY_PERMISSIONS } from '@/constants/permissions'

/** 编码录入：诊断/手术名称 → 主导词全量匹配拆分 → 修饰词下钻 → 有效编码推荐。 */
const codingEntryRoutes: RouteRecordRaw[] = [
  {
    path: '/coding-entry',
    name: 'CodingEntry',
    component: () => import('@/views/CodingEntry.vue'),
    meta: {
      titleKey: 'menu.codingEntry',
      title: 'Coding Entry',
      icon: ClipboardList,
      order: 4,
      keepAlive: true,
      permissions: CASE_ENTRY_PERMISSIONS,
    },
  },
]

export default codingEntryRoutes
