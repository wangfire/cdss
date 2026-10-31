import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

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

/**
 * 通知状态管理 Store（WP-C 前端生产化：去 mock）
 *
 * 初始化为空列表——本系统当前无推送基础设施（无 SignalR/轮询事件源），
 * 通知中心作为本地会话通知容器：批量管道任务完成时由任务提交方
 * （前端轮询 case-entry 结果后）调用 addNotification 推送，
 * 其他业务方也可按需调用。刷新页面后会话通知清空（设计如此，非持久化）。
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
})
