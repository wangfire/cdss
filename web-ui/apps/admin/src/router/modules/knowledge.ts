import type { RouteRecordRaw } from 'vue-router'
import { BookOpen } from 'lucide-vue-next'

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
    },
  },
]

export default knowledgeRoutes
