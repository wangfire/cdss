import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { STORAGE_KEYS } from '@/constants/common'

export type NotificationType = 'info' | 'success' | 'warning' | 'error' | 'message'

export interface Notification {
  id: string
  title: string
  message: string
  type: NotificationType
  read: boolean
  createdAt: Date
  actionUrl?: string
  actionLabel?: string
}

/** 本地持久化上限：防止 localStorage 无限增长（最旧的在 unshift 时被挤出）。 */
const MAX_NOTIFICATIONS = 50

/**
 * 通知状态管理 Store（WP-C 前端生产化：去 mock）
 *
 * 初始化为空列表——本系统当前无推送基础设施（无 SignalR/轮询事件源），
 * 通知中心作为本地会话通知容器：批量管道任务完成时由任务提交方
 * （前端轮询 case-entry 结果后）调用 addNotification 推送，
 * 其他业务方也可按需调用。
 *
 * 持久化（2026-11-15）：经 pinia-plugin-persistedstate 写 localStorage
 * （上限 50 条），刷新页面后通知保留。createdAt 经 JSON 序列化会变成
 * ISO 字符串，deserialize 时复活为 Date，排序与展示不感知差异。
 */
export const useNotificationStore = defineStore('notification', () => {
  const notifications = ref<Notification[]>([])

  const unreadCount = computed(() => {
    return notifications.value.filter((n) => !n.read).length
  })

  const notificationList = computed(() => {
    return [...notifications.value].sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime())
  })

  const typeCount = computed(() => {
    const counts: Record<NotificationType, number> = {
      info: 0,
      success: 0,
      warning: 0,
      error: 0,
      message: 0,
    }
    notifications.value.forEach((n) => {
      counts[n.type]++
    })
    return counts
  })

  function addNotification(notification: Omit<Notification, 'id' | 'createdAt' | 'read'>) {
    const newNotification: Notification = {
      ...notification,
      id: `notification-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      createdAt: new Date(),
      read: false,
    }
    notifications.value.unshift(newNotification)
    if (notifications.value.length > MAX_NOTIFICATIONS) {
      notifications.value.length = MAX_NOTIFICATIONS
    }
  }

  function markAsRead(id: string) {
    const notification = notifications.value.find((n) => n.id === id)
    if (notification) {
      notification.read = true
    }
  }

  function markAllAsRead() {
    notifications.value.forEach((n) => {
      n.read = true
    })
  }

  function removeNotification(id: string) {
    const index = notifications.value.findIndex((n) => n.id === id)
    if (index > -1) {
      notifications.value.splice(index, 1)
    }
  }

  function clearAll() {
    notifications.value = []
  }

  return {
    notifications,
    unreadCount,
    notificationList,
    typeCount,
    addNotification,
    markAsRead,
    markAllAsRead,
    removeNotification,
    clearAll,
  }
},
{
  persist: {
    key: STORAGE_KEYS.NOTIFICATION,
    pick: ['notifications'],
    // createdAt 序列化为 ISO 字符串；恢复时复活为 Date，
    // 使 notificationList 的排序与 Notification.vue 的 formatTime 不感知差异。
    serializer: {
      serialize: (state) => JSON.stringify(state),
      deserialize: (value) => {
        const state = JSON.parse(value) as {
          notifications?: Array<{ createdAt: string } & Record<string, unknown>>
        }
        for (const notification of state.notifications ?? []) {
          ;(notification as unknown as { createdAt: Date }).createdAt = new Date(notification.createdAt)
        }
        return state
      },
    },
  },
})
