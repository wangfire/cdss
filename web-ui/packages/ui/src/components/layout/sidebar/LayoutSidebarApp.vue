<script setup lang="ts">
import type { SidebarConfig, LayoutSidebarMenuItem } from './config'
import { computed, ref, watch, onUnmounted } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import { useLayoutSidebar } from '../composables'
import { defaultSidebarConfig } from './config'
import LayoutSidebarItem from './LayoutSidebarItem.vue'
import LayoutSidebarSubMenu from './LayoutSidebarSubMenu.vue'
import LayoutSidebarMobile from './LayoutSidebarMobile.vue'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '../../ui/tooltip'
import { ScrollArea } from '../../ui/scroll-area'
import { PanelLeftClose, PanelRight } from 'lucide-vue-next'

/**
 * LayoutSidebarApp - 侧边栏入口组件
 * 响应式切换桌面端/移动端
 */

interface Props {
  /** 菜单列表 */
  menus?: LayoutSidebarMenuItem[]
  /** 是否折叠 */
  collapsed?: boolean
  /** 侧边栏宽度 */
  width?: number
  /** 自定义类名 */
  class?: HTMLAttributes['class']
  /** 当前激活的菜单项 key */
  activeId?: string
}

import type { HTMLAttributes } from 'vue'

const props = withDefaults(defineProps<Props>(), {
  menus: () => [],
  collapsed: undefined,
  width: undefined,
  activeId: undefined,
})

const emit = defineEmits<{
  /** 更新折叠状态 */
  (e: 'update:collapsed', value: boolean): void
  /** 切换折叠 */
  (e: 'toggleCollapse'): void
  /** 导航 */
  (e: 'navigate', path: string): void
}>()

defineSlots<{
  /** Logo 插槽 */
  logo(): void
  /** 侧栏标题插槽 */
  'sidebar-title'(): void
  /** 折叠按钮插槽 */
  'collapse-button'(): void
  /** 展开按钮插槽 */
  'expand-button'(): void
  /** 底部插槽 */
  footer(props: { collapsed: boolean }): void
}>()

/**
 * 是否为桌面端
 */
const isDesktop = useMediaQuery('(min-width: 1024px)')

/**
 * 动态侧栏配置
 */
const dynamicSidebarConfig = computed<SidebarConfig>(() => ({
  ...defaultSidebarConfig,
  menus: props.menus,
  defaultWidth: props.width ?? defaultSidebarConfig.defaultWidth,
}))

/**
 * 使用侧栏逻辑
 */
const sidebarState = useLayoutSidebar(dynamicSidebarConfig.value)

/**
 * 折叠状态 - 优先使用外部传入的值
 */
const collapsed = computed({
  get: () => props.collapsed ?? sidebarState.collapsed.value,
  set: (value) => {
    sidebarState.collapsed.value = value
    emit('update:collapsed', value)
  },
})

/**
 * 处理折叠切换
 */
function handleToggleCollapse(): void {
  sidebarState.toggleCollapse()
  emit('update:collapsed', sidebarState.collapsed.value)
  emit('toggleCollapse')
}

/**
 * 处理导航
 */
function handleNavigate(path: string): void {
  emit('navigate', path)
}

/**
 * 预计算所有菜单项的激活状态
 * 参考 DoubleSidebarMenu 的实现方式
 */
const activeStates = computed(() => {
  const states = new Map<string, boolean>()

  function checkSubtree(item: LayoutSidebarMenuItem, targetId?: string): boolean {
    if (!targetId) return false
    if (item.key === targetId) return true
    if (item.children) {
      return item.children.some((child) => checkSubtree(child, targetId))
    }
    return false
  }

  for (const item of props.menus) {
    states.set(item.key, checkSubtree(item, props.activeId))
  }

  return states
})

/**
 * 获取菜单项的激活状态
 */
function isMenuActive(item: LayoutSidebarMenuItem): boolean {
  return activeStates.value.get(item.key) ?? false
}

/**
 * 判断菜单是否有子项处于激活状态
 */
function hasActiveChild(item: LayoutSidebarMenuItem): boolean {
  if (!item.children?.length) return false
  return item.children.some((child) => activeStates.value.get(child.key) ?? false)
}

/**
 * 用户手动展开的菜单 keys
 * 参考 DoubleSidebar 使用 ref 管理展开状态
 */
const expandedKeys = ref<Set<string>>(new Set())

/**
 * 切换展开状态
 */
function toggleSubMenu(key: string) {
  if (expandedKeys.value.has(key)) {
    expandedKeys.value.delete(key)
  } else {
    expandedKeys.value.add(key)
  }
}

/**
 * 判断子菜单是否应该展开
 * 如果当前激活项是该菜单的子项，则强制展开
 * 否则使用用户手动展开的状态
 */
function isMenuExpanded(item: LayoutSidebarMenuItem): boolean {
  if (!item.children?.length) return false

  // 如果当前激活项是该菜单的子项，则强制展开
  if (props.activeId && item.children.some((child) => child.key === props.activeId)) {
    return true
  }

  // 否则，使用用户手动展开的状态
  return expandedKeys.value.has(item.key)
}

/**
 * watch 监听 activeId 变化，自动展开包含激活项的父菜单
 * 参考 DoubleSidebar 的实现
 */
watch(
  () => props.activeId,
  (newId) => {
    if (!newId) return
    for (const item of props.menus) {
      if (item.children?.some((child) => child.key === newId)) {
        expandedKeys.value.add(item.key)
      }
    }
  },
  { immediate: true },
)

/**
 * 移动端侧边栏打开状态
 */
const mobileOpen = computed({
  get: () => !isDesktop.value && !collapsed.value,
  set: (value) => {
    if (!isDesktop.value) {
      collapsed.value = !value
    }
  },
})

/**
 * 侧栏宽度样式
 */
const sidebarStyle = computed(() => {
  if (collapsed.value) {
    return { width: `${dynamicSidebarConfig.value.collapsedWidth}px` }
  }
  return { width: `${sidebarState.currentWidth.value}px` }
})

/**
 * 是否正在拖拽
 */
const isDragging = ref(false)

/**
 * 拖拽开始位置
 */
let dragStartX = 0
let dragStartWidth = 0

/**
 * RAF ID 用于取消未执行的帧
 */
let rafId: number | null = null

/**
 * 待更新的宽度值
 */
let pendingWidth: number | null = null

/**
 * 处理拖拽开始
 */
function handleDragStart(event: MouseEvent): void {
  if (collapsed.value) return

  isDragging.value = true
  sidebarState.isDragging.value = true
  dragStartX = event.clientX
  dragStartWidth = sidebarState.currentWidth.value

  document.addEventListener('mousemove', handleDragMove)
  document.addEventListener('mouseup', handleDragEnd)
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
}

/**
 * 处理拖拽移动 - 使用 RAF 节流
 */
function handleDragMove(event: MouseEvent): void {
  if (!isDragging.value) return

  const deltaX = event.clientX - dragStartX
  const newWidth = dragStartWidth + deltaX

  const minWidth = dynamicSidebarConfig.value.minWidth
  const maxWidth = dynamicSidebarConfig.value.maxWidth

  pendingWidth = Math.max(minWidth, Math.min(maxWidth, newWidth))

  // 如果没有待执行的 RAF，则调度一个
  if (rafId === null) {
    rafId = requestAnimationFrame(flushUpdate)
  }
}

/**
 * 刷新更新 - 在 RAF 中执行
 */
function flushUpdate(): void {
  rafId = null

  if (pendingWidth !== null) {
    sidebarState.size.value = (pendingWidth / window.innerWidth) * 100
    pendingWidth = null
  }
}

/**
 * 处理拖拽结束
 */
function handleDragEnd(): void {
  isDragging.value = false
  sidebarState.isDragging.value = false

  // 取消未执行的 RAF
  if (rafId !== null) {
    cancelAnimationFrame(rafId)
    rafId = null
  }

  document.removeEventListener('mousemove', handleDragMove)
  document.removeEventListener('mouseup', handleDragEnd)
  document.body.style.cursor = ''
  document.body.style.userSelect = ''
}

/**
 * 清理拖拽事件监听器
 */
function cleanupDragListeners(): void {
  if (isDragging.value) {
    // 取消未执行的 RAF
    if (rafId !== null) {
      cancelAnimationFrame(rafId)
      rafId = null
    }
    document.removeEventListener('mousemove', handleDragMove)
    document.removeEventListener('mouseup', handleDragEnd)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
  }
}

/**
 * 监听 props.width 变化，同步到 sidebarState
 * 注意：不使用 immediate: true，因为状态已在 useSidebar 中同步恢复
 */
watch(
  () => props.width,
  (newWidth, oldWidth) => {
    if (newWidth !== undefined && !isDragging.value && oldWidth !== undefined) {
      const percentSize = (newWidth / window.innerWidth) * 100
      sidebarState.size.value = percentSize
    }
  },
)

/**
 * 组件卸载时清理拖拽事件监听器
 */
onUnmounted(() => {
  cleanupDragListeners()
})
</script>

<template>
  <!-- 根容器 - 包装桌面端和移动端侧栏作为单一根节点 -->
  <div>
    <!-- 桌面端侧栏 -->
    <aside
      v-if="isDesktop"
      class="h-full flex-shrink-0 border-r border-border/30 bg-background/80 backdrop-blur-xl supports-[backdrop-filter]:bg-background/70 relative flex flex-col transition-all duration-200"
      :class="[props.class, { 'select-none': isDragging }]"
      :style="sidebarStyle"
    >
      <!-- 菜单区域背景装饰 -->
      <div v-if="!collapsed" class="absolute inset-0 pointer-events-none">
        <div
          class="absolute top-0 left-0 right-0 h-32 bg-gradient-to-b from-primary/3 to-transparent"
        />
        <div
          class="absolute bottom-0 left-0 right-0 h-32 bg-gradient-to-t from-muted/10 to-transparent"
        />
      </div>

      <!-- Logo 区域 -->
      <div class="relative z-10 border-b border-border/30" :class="collapsed ? 'p-2' : 'p-3'">
        <div
          class="flex items-center transition-all duration-200"
          :class="collapsed ? 'justify-center' : 'gap-3'"
        >
          <slot name="logo">
            <div
              class="flex items-center justify-center rounded-lg transition-colors duration-150"
              :class="
                collapsed
                  ? 'h-12 w-12 bg-primary text-primary-foreground'
                  : 'h-10 w-10 bg-primary/10'
              "
              :style="{
                filter: 'drop-shadow(0 1px 2px rgba(0, 0, 0, 0.15))',
                transform: 'translateZ(0)',
              }"
            >
              <svg
                class="block"
                :width="collapsed ? 28 : 24"
                :height="collapsed ? 28 : 24"
                viewBox="0 0 48 48"
                fill="none"
                xmlns="http://www.w3.org/2000/svg"
                :style="{ transform: 'translateZ(0)' }"
              >
                <defs>
                  <linearGradient id="logoGradient" x1="0%" y1="0%" x2="100%" y2="100%">
                    <stop offset="0%" :stop-color="'var(--primary)'" stop-opacity="1" />
                    <stop offset="100%" :stop-color="'var(--primary)'" stop-opacity="0.85" />
                  </linearGradient>
                </defs>
                <rect x="4" y="4" width="18" height="18" rx="4" fill="url(#logoGradient)" />
                <g class="text-foreground">
                  <rect
                    x="26"
                    y="4"
                    width="18"
                    height="18"
                    rx="4"
                    fill="currentColor"
                    opacity="0.9"
                  />
                </g>
                <rect
                  x="4"
                  y="26"
                  width="18"
                  height="18"
                  rx="4"
                  fill="url(#logoGradient)"
                  opacity="0.7"
                />
                <rect
                  x="26"
                  y="26"
                  width="18"
                  height="18"
                  rx="4"
                  fill="url(#logoGradient)"
                  opacity="0.5"
                />
              </svg>
            </div>
          </slot>
          <slot name="sidebar-title">
            <div v-if="!collapsed" class="flex flex-col min-w-0">
              <span class="text-sm font-bold tracking-tight truncate">TabTab Admin</span>
              <span class="text-[10px] text-muted-foreground truncate">管理系统</span>
            </div>
          </slot>
        </div>

        <!-- 折叠按钮 - Logo 下方 -->
        <!-- 展开状态：直接渲染按钮，无 Tooltip 包裹 -->
        <button
          v-if="!collapsed"
          class="mt-2 w-full h-8 flex items-center justify-center rounded-lg bg-muted/50 hover:bg-muted hover:text-primary transition-all duration-200 gap-2"
          @click="handleToggleCollapse"
        >
          <PanelLeftClose class="h-4 w-4" />
          <slot name="collapse-button">
            <span class="text-xs text-muted-foreground">收起侧栏</span>
          </slot>
        </button>

        <!-- 折叠状态：使用 Tooltip 提供悬停提示 -->
        <TooltipProvider v-else :delay-duration="200">
          <Tooltip>
            <TooltipTrigger as-child>
              <button
                class="mt-2 w-full h-8 flex items-center justify-center rounded-lg bg-muted/50 hover:bg-muted hover:text-primary transition-all duration-200 px-0"
                @click="handleToggleCollapse"
              >
                <PanelRight class="h-4 w-4" />
              </button>
            </TooltipTrigger>
            <TooltipContent side="right">
              <slot name="expand-button">
                <span>展开侧栏</span>
              </slot>
            </TooltipContent>
          </Tooltip>
        </TooltipProvider>
      </div>

      <!-- 菜单列表 -->
      <ScrollArea class="flex-1 min-h-0 relative z-10">
        <nav class="p-3 space-y-1">
          <template v-for="item in dynamicSidebarConfig.menus" :key="item.key">
            <LayoutSidebarSubMenu
              v-if="item.children && item.children.length > 0"
              :item="item"
              :collapsed="collapsed"
              :active="isMenuActive(item)"
              :has-active-child="hasActiveChild(item)"
              :expanded="isMenuExpanded(item)"
              :active-id="props.activeId"
              @toggle="toggleSubMenu(item.key)"
              @navigate="handleNavigate"
            />
            <LayoutSidebarItem
              v-else
              :item="item"
              :title="item.title"
              :collapsed="collapsed"
              :active="isMenuActive(item)"
              @navigate="handleNavigate"
            />
          </template>
        </nav>
      </ScrollArea>

      <!-- 底部区域 -->
      <div class="border-t border-border/30 flex-shrink-0 min-h-[70px]">
        <slot name="footer" :collapsed="collapsed" />
      </div>

      <!-- 拖拽手柄 -->
      <div
        v-if="!collapsed"
        class="absolute top-0 right-0 bottom-0 w-2 cursor-col-resize group hover:bg-primary/20 transition-colors z-20"
        :class="{ 'bg-primary/30': isDragging }"
        @mousedown.prevent="handleDragStart"
      >
        <div
          class="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-0.5 h-8 rounded-full bg-border/60 group-hover:bg-primary/50 transition-colors pointer-events-none"
          :class="{ 'bg-primary/60': isDragging }"
        />
      </div>
    </aside>

    <!-- 移动端侧栏 -->
    <template v-else>
      <LayoutSidebarMobile
        :config="dynamicSidebarConfig"
        :expanded-keys="expandedKeys"
        :open="mobileOpen"
        @update:open="mobileOpen = $event"
        @toggle-sub-menu="toggleSubMenu"
        @navigate="handleNavigate"
      >
        <template #logo>
          <slot name="logo" />
        </template>
        <template #footer>
          <slot name="footer" :collapsed="false" />
        </template>
      </LayoutSidebarMobile>
    </template>
  </div>
</template>
