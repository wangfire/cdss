/** * ToastItem 组件 * 显示单个 Toast 通知项 */

<script lang="ts" setup>
import { computed, onMounted, ref, watch } from 'vue'
import {
  CircleCheckIcon,
  InfoIcon,
  Loader2Icon,
  OctagonXIcon,
  TriangleAlertIcon,
  XIcon,
} from 'lucide-vue-next'
import { cn } from '@tabtab/utils'
import type { ToastData, ToastType } from './types'

interface Props {
  toast: ToastData
  showProgress?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  showProgress: true,
})

const emit = defineEmits<{
  (e: 'dismiss', id: string): void
}>()

const progressWidth = ref(100)
const animationFrame = ref<number | null>(null)
const startTime = ref<number>(0)

/**
 * 根据类型获取图标组件
 */
const getIconComponent = (type: ToastType) => {
  switch (type) {
    case 'success':
      return CircleCheckIcon
    case 'error':
      return OctagonXIcon
    case 'warning':
      return TriangleAlertIcon
    case 'info':
      return InfoIcon
    case 'loading':
      return Loader2Icon
    default:
      return InfoIcon
  }
}

/**
 * 根据类型获取图标颜色类
 */
const getIconColorClass = (type: ToastType): string => {
  switch (type) {
    case 'success':
      return 'text-primary'
    case 'error':
      return 'text-destructive'
    case 'warning':
      return 'text-ring'
    case 'info':
      return 'text-primary'
    case 'loading':
      return 'text-muted-foreground'
    default:
      return 'text-muted-foreground'
  }
}

/**
 * 处理关闭按钮点击
 */
const handleDismiss = () => {
  emit('dismiss', props.toast.id)
}

/**
 * 处理 Toast 点击
 */
const handleClick = () => {
  props.toast.onClick?.()
}

/**
 * 更新进度条
 */
const updateProgress = () => {
  if (props.toast.duration === 0 || props.toast.important) {
    progressWidth.value = 100
    return
  }

  const elapsed = Date.now() - startTime.value
  const remaining = Math.max(0, props.toast.duration - elapsed)
  progressWidth.value = (remaining / props.toast.duration) * 100

  if (remaining > 0 && props.toast.visible) {
    animationFrame.value = requestAnimationFrame(updateProgress)
  }
}

/**
 * 开始进度条动画
 */
const startProgress = () => {
  if (props.toast.duration === 0 || props.toast.important) return

  startTime.value = Date.now()
  progressWidth.value = 100
  animationFrame.value = requestAnimationFrame(updateProgress)
}

/**
 * 停止进度条动画
 */
const stopProgress = () => {
  if (animationFrame.value) {
    cancelAnimationFrame(animationFrame.value)
    animationFrame.value = null
  }
}

onMounted(() => {
  if (props.toast.visible) {
    startProgress()
  }
})

watch(
  () => props.toast.visible,
  (visible) => {
    if (visible) {
      startProgress()
    } else {
      stopProgress()
    }
  },
)

watch(
  () => props.toast.duration,
  () => {
    stopProgress()
    if (props.toast.visible) {
      startProgress()
    }
  },
)

const toastClasses = computed(() => {
  return cn(
    'group relative flex items-start gap-3 overflow-hidden rounded-lg border p-4 shadow-lg',
    'transition-all duration-200 ease-in-out',
    'bg-popover text-popover-foreground border-border',
    'hover:shadow-xl',
    'min-w-[280px] max-w-[420px]',
    'w-auto',
    props.toast.class,
  )
})

const IconComponent = computed(() => {
  return props.toast.icon || getIconComponent(props.toast.type)
})

const iconAnimationClass = computed(() => {
  return props.toast.type === 'loading' ? 'animate-spin' : ''
})
</script>

<template>
  <div :class="toastClasses" @click="handleClick">
    <!-- 图标 -->
    <div class="flex-shrink-0 mt-0.5">
      <component
        :is="IconComponent"
        :class="cn('size-4', getIconColorClass(toast.type), iconAnimationClass)"
      />
    </div>

    <!-- 内容 -->
    <div class="flex-1 min-w-0 pr-6">
      <div class="text-sm font-medium leading-5">
        {{ toast.title }}
      </div>
      <div v-if="toast.description" class="mt-1 text-sm text-muted-foreground leading-5">
        {{ toast.description }}
      </div>
    </div>

    <!-- 关闭按钮 -->
    <button
      v-if="toast.closeButton"
      type="button"
      class="absolute right-2 top-2 flex-shrink-0 rounded-sm p-1 text-muted-foreground/70 opacity-0 transition-all duration-200 hover:bg-accent hover:text-foreground group-hover:opacity-100 focus:opacity-100 focus:outline-none focus:ring-1 focus:ring-ring"
      :class="{ 'opacity-100': !toast.visible }"
      @click.stop="handleDismiss"
    >
      <XIcon class="size-3.5" />
    </button>

    <!-- 进度条 -->
    <div
      v-if="showProgress && toast.duration > 0 && !toast.important"
      class="absolute bottom-0 left-0 h-[2px] bg-primary/30 transition-all duration-100 ease-linear"
      :style="{ width: `${progressWidth}%` }"
    />
  </div>
</template>
