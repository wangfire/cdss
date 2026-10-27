import type { RouteRecordRaw } from 'vue-router'
import { BookOpen } from 'lucide-vue-next'
import { KNOWLEDGE_PERMISSIONS } from '@/constants/permissions'

const knowledgeRoutes: RouteRecordRaw[] = [
  {
    path: '/knowledge',
    name: 'Knowledge',
    component: () => import('@/views/Knowledge.vue'),
    meta: {
      titleKey: 'menu.knowledge',
      title: 'Knowledge',
      icon: BookOpen,
      order: 4,
      keepAlive: true,
      permissions: KNOWLEDGE_PERMISSIONS,
    },
  },
]

export default knowledgeRoutes
