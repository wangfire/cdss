<script setup lang="ts">
import type { LayoutSidebarMenuItem } from './config'
import { computed, ref } from 'vue'
import { useElementBounding, useWindowSize, useWindowScroll, useEventListener } from '@vueuse/core'
import { useTimeoutFn } from '@vueuse/core'
import { ChevronDown } from 'lucide-vue-next'
import { Button } from '../../ui/button'
import { Badge } from '../../ui/badge'
import { ScrollArea } from '../../ui/scroll-area'
import { formatBadge } from '../composables'
import { isComponent } from '../composables'
import LayoutSidebarMenuItemRecursive from './LayoutSidebarMenuItemRecursive.vue'

/**
 * LayoutSidebarSubMenu - 多级菜单项组件
 * 折叠状态：悬停显示子菜单弹窗（Teleport）
 * 展开状态：树形展开/收起
 */

interface PopoverPosition {
  top: number
  left: number
}

interface Props {
  /** 菜单项数据 */
  item: LayoutSidebarMenuItem
  /** 是否折叠 */
  collapsed: boolean
  /** 是否激活 */
  active: boolean
  /** 是否展开 */
  expanded: boolean
  /** 是否有子项激活 */
  hasActiveChild?: boolean
  /** 当前激活的菜单项 key */
  activeId?: string
}

const props = withDefaults(defineProps<Props>(), {
  hasActiveChild: false,
  activeId: undefined,
})

const emit = defineEmits<{
  /** 切换展开 */
  (e: 'toggle'): void
  /** 导航事件 */
  (e: 'navigate', path: string): void
}>()

/**
 * 按钮元素引用
 */
const buttonRef = ref<HTMLElement | null>(null)

/**
 * 获取按钮位置
 */
const { top, left, width, update: updateBounding } = useElementBounding(buttonRef)

/**
 * 窗口滚动位置
 */
const { y: scrollY } = useWindowScroll()

/**
 * 窗口尺寸
 */
const { height: windowHeight } = useWindowSize()

/**
 * 弹窗位置 - 考虑滚动偏移和视口边界
 */
const popoverPosition = computed<PopoverPosition>(() => {
  const rawTop = top.value - scrollY.value
  const estimatedPopoverHeight = 470
  const maxTop = windowHeight.value - estimatedPopoverHeight - 20

  const adjustedTop = maxTop > 0 ? Math.min(rawTop, maxTop) : 10

  return {
    top: Math.max(10, adjustedTop),
    left: left.value + width.value + 8,
  }
})

/**
 * 折叠状态下的子菜单弹窗显示控制
 */
const showPopover = ref(false)

/**
 * 使用 useEventListener 替代手动事件监听
 * 当 showPopover 为 true 时自动监听 scroll 事件
 */
useEventListener(
  window,
  'scroll',
  () => {
    if (showPopover.value) {
      updateBounding()
    }
  },
  { passive: true },
)

/**
 * 延迟关闭弹窗的 timeout 控制
 */
const { start: startHidePopover, stop: stopHidePopover } = useTimeoutFn(() => {
  showPopover.value = false
}, 150)

/**
 * 处理父菜单点击
 */
function handleParentClick(): void {
  if (!props.collapsed) {
    emit('toggle')
  }
}

/**
 * 处理子菜单导航
 */
function handleChildNavigate(path: string): void {
  emit('navigate', path)
  showPopover.value = false
}

/**
 * 处理鼠标进入（折叠状态）
 */
function handleMouseEnter(): void {
  if (props.collapsed) {
    stopHidePopover()
    showPopover.value = true
  }
}

/**
 * 处理鼠标离开（折叠状态）
 */
function handleMouseLeave(): void {
  if (props.collapsed) {
    startHidePopover()
  }
}

/**
 * 按钮变体
 */
const variant = computed(() => {
  if (props.active || props.hasActiveChild) {
    return 'default'
  }
  return 'ghost'
})

/**
 * 菜单标题
 */
const menuTitle = computed(() => props.item.title)

/**
 * ARIA 标签（折叠状态下使用）
 */
const ariaLabel = computed(() => {
  if (!props.collapsed) {
    return undefined
  }
  const childCount = props.item.children?.length ?? 0
  return props.item.badge
    ? `${menuTitle.value} (${props.item.badge} 条通知, ${childCount} 个子菜单)`
    : `${menuTitle.value} (${childCount} 个子菜单)`
})
</script>

<template>
  <!-- 折叠状态：悬停显示子菜单弹窗 -->
  <div
    v-if="collapsed"
    ref="buttonRef"
    class="relative"
    @mouseenter="handleMouseEnter"
    @mouseleave="handleMouseLeave"
  >
    <Button
      :variant="variant"
      size="icon"
      role="menuitem"
      :aria-label="ariaLabel"
      :aria-expanded="showPopover"
      :aria-haspopup="true"
      class="relative h-10 w-10 transition-colors duration-200 focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 rounded-lg"
      :class="[
        active || hasActiveChild
          ? 'bg-primary text-primary-foreground'
          : 'hover:bg-primary/10 hover:text-primary',
      ]"
    >
      <component
        :is="item.icon"
        v-if="item.icon && isComponent(item.icon)"
        class="h-5 w-5"
        aria-hidden="true"
      />

      <Badge
        v-if="item.badge"
        variant="destructive"
        class="absolute -top-1 left-1 h-4 min-w-4 !px-1 text-[10px] animate-in zoom-in-50"
        role="status"
      >
        {{ item.badge }}
      </Badge>

      <span
        v-else-if="item.children?.length"
        class="absolute -bottom-1 -right-1 h-4 min-w-4 px-1 text-[10px] font-medium flex items-center justify-center rounded-full border z-10"
        :class="
          active || hasActiveChild
            ? 'bg-primary-foreground text-primary border-primary-foreground/50'
            : 'bg-primary/10 text-primary border-primary/20'
        "
        aria-hidden="true"
      >
        {{ item.children.length }}
      </span>

      <span
        v-if="(active || hasActiveChild) && !item.children?.length"
        class="absolute -bottom-0.5 -right-0.5 h-2.5 w-2.5 rounded-full border-2 border-background bg-primary-foreground"
        aria-hidden="true"
      />
    </Button>

    <!-- 子菜单弹窗 -->
    <Teleport to="body">
      <Transition
        enter-active-class="transition-all duration-250 ease-out"
        enter-from-class="opacity-0 scale-95 -translate-x-3"
        enter-to-class="opacity-100 scale-100 translate-x-0"
        leave-active-class="transition-all duration-200 ease-in"
        leave-from-class="opacity-100 scale-100 translate-x-0"
        leave-to-class="opacity-0 scale-95 -translate-x-3"
      >
        <div
          v-if="showPopover"
          role="menu"
          :aria-label="`${menuTitle} 子菜单`"
          class="fixed w-56 bg-popover/95 backdrop-blur-sm border border-border/50 z-[9999] overflow-hidden rounded-xl"
          :style="{
            top: `${popoverPosition.top}px`,
            left: `${popoverPosition.left}px`,
            boxShadow:
              '0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04), 0 0 0 1px rgba(var(--primary), 0.05)',
          }"
          @mouseenter="(stopHidePopover(), (showPopover = true))"
          @mouseleave="startHidePopover()"
        >
          <!-- 箭头指示器 -->
          <div
            class="absolute left-0 top-4 w-2 h-2 bg-popover border-l border-b border-border/50 transform -translate-x-1 rotate-45"
          />

          <!-- 标题区域 -->
          <div
            class="relative px-4 py-3 border-b border-border/40 bg-gradient-to-r from-muted/30 to-transparent"
          >
            <div class="flex items-center gap-2.5">
              <div
                class="h-8 w-8 rounded-lg flex items-center justify-center flex-shrink-0"
                :class="active || hasActiveChild ? 'bg-primary/15' : 'bg-muted'"
              >
                <component
                  :is="item.icon"
                  v-if="item.icon && isComponent(item.icon)"
                  class="h-4 w-4"
                  :class="active || hasActiveChild ? 'text-primary' : 'text-muted-foreground'"
                />
              </div>
              <div class="flex-1 min-w-0">
                <p class="text-sm font-semibold text-foreground truncate">
                  {{ menuTitle }}
                </p>
                <p class="text-[10px] text-muted-foreground">{{ item.children?.length }} 项</p>
              </div>
            </div>
          </div>

          <!-- 子菜单列表 -->
          <ScrollArea :class="item.children && item.children.length > 8 ? 'h-[320px]' : ''">
            <div class="p-2 space-y-0.5" role="group" :aria-label="`${menuTitle} 子菜单项`">
              <LayoutSidebarMenuItemRecursive
                v-for="child in item.children"
                :key="child.key"
                :item="child"
                :collapsed="false"
                :level="1"
                :active-id="props.activeId"
                @navigate="handleChildNavigate"
              />
            </div>
          </ScrollArea>

          <!-- 底部装饰线 -->
          <div class="h-1 bg-gradient-to-r from-primary/20 via-primary/10 to-transparent" />
        </div>
      </Transition>
    </Teleport>
  </div>

  <!-- 展开状态：树形结构 -->
  <div v-else class="space-y-0.5">
    <!-- 父菜单 -->
    <Button
      :variant="variant"
      role="menuitem"
      :aria-expanded="expanded"
      :aria-haspopup="true"
      :aria-current="active ? 'page' : undefined"
      class="w-full justify-between h-9 px-3 group transition-colors duration-150 rounded-lg"
      :class="[
        active || hasActiveChild
          ? 'bg-primary/10 text-primary hover:bg-primary/15'
          : 'hover:bg-accent hover:text-accent-foreground',
      ]"
      @click="handleParentClick"
    >
      <div class="flex items-center gap-2 flex-1 min-w-0">
        <component
          :is="item.icon"
          v-if="item.icon && isComponent(item.icon)"
          class="h-4 w-4"
          :class="[
            active || hasActiveChild
              ? 'text-primary'
              : 'text-muted-foreground group-hover:text-accent-foreground',
          ]"
          aria-hidden="true"
        />
        <span class="truncate text-sm">{{ menuTitle }}</span>
      </div>

      <div class="flex items-center gap-1.5 flex-shrink-0">
        <Badge
          v-if="item.badge"
          variant="destructive"
          class="h-4 px-1.5 text-[10px] font-medium"
          role="status"
        >
          {{ item.badge }}
        </Badge>

        <ChevronDown
          class="h-3.5 w-3.5 transition-transform duration-200"
          :class="[
            expanded ? 'rotate-0' : '-rotate-90',
            active || hasActiveChild ? 'text-primary' : 'text-muted-foreground',
          ]"
          aria-hidden="true"
        />
      </div>
    </Button>

    <!-- 子菜单 -->
    <Transition
      enter-active-class="transition-all duration-200 ease-out"
      enter-from-class="opacity-0 max-h-0"
      enter-to-class="opacity-100 max-h-[500px]"
      leave-active-class="transition-all duration-150 ease-in"
      leave-from-class="opacity-100 max-h-[500px]"
      leave-to-class="opacity-0 max-h-0"
    >
      <div
        v-if="expanded"
        role="menu"
        :aria-label="`${menuTitle} 子菜单`"
        class="ml-4 mt-1 space-y-0.5 overflow-hidden relative border-l border-border/50 pl-3"
      >
        <LayoutSidebarMenuItemRecursive
          v-for="child in item.children"
          :key="child.key"
          :item="child"
          :collapsed="collapsed"
          :level="1"
          :active-id="props.activeId"
          @navigate="handleChildNavigate"
        />
      </div>
    </Transition>
  </div>
</template>
