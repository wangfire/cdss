<script setup lang="ts">
import type { LayoutSidebarMenuItem } from './config'
import { computed, ref } from 'vue'
import { ChevronDown, ChevronRight } from 'lucide-vue-next'
import { Button } from '../../ui/button'
import { Badge } from '../../ui/badge'
import { formatBadge } from '../composables'
import { isComponent } from '../composables'

/**
 * LayoutSidebarMenuItemRecursive - 递归菜单渲染组件
 * 支持无限层级嵌套
 */

interface Props {
  /** 菜单项数据 */
  item: LayoutSidebarMenuItem
  /** 是否折叠 */
  collapsed: boolean
  /** 当前层级 */
  level?: number
  /** 当前激活的菜单项 key */
  activeId?: string
}

const props = withDefaults(defineProps<Props>(), {
  level: 0,
  activeId: undefined,
})

const emit = defineEmits<{
  /** 导航事件 */
  (e: 'navigate', path: string): void
}>()

/**
 * 是否展开
 */
const isExpanded = ref(props.item.defaultExpanded ?? false)

/**
 * 检查当前项或其任意子项是否匹配 targetId
 * 递归检查所有层级，支持无限级菜单
 */
function checkSubtree(item: LayoutSidebarMenuItem, targetId: string): boolean {
  if (item.key === targetId) return true
  if (item.children) {
    return item.children.some((child) => checkSubtree(child, targetId))
  }
  return false
}

/**
 * 当前项是否激活
 */
const isActive = computed(() => {
  if (!props.activeId) return false
  return props.item.key === props.activeId
})

/**
 * 是否有子项激活（递归检查所有子层级）
 */
const hasActiveChild = computed(() => {
  if (!props.activeId || !props.item.children) return false
  return props.item.children.some((child) => checkSubtree(child, props.activeId!))
})

/**
 * 是否有子菜单
 */
const hasChildren = computed(() => {
  return !!props.item.children && props.item.children.length > 0
})

/**
 * 获取按钮样式类
 */
const buttonClasses = computed(() => {
  const baseClasses = 'w-full justify-between h-9 px-3 group transition-all duration-200 rounded-lg'

  if (props.item.disabled) {
    return `${baseClasses} opacity-50 cursor-not-allowed`
  }

  if (isActive.value) {
    return `${baseClasses} bg-primary/10 text-primary font-medium shadow-[inset_3px_0_0_0_hsl(var(--primary))] hover:bg-primary/15`
  }
  if (hasActiveChild.value) {
    return `${baseClasses} bg-muted/30 text-foreground shadow-[inset_3px_0_0_0_hsl(var(--primary)/0.3)] hover:bg-muted/50`
  }
  return `${baseClasses} hover:bg-accent hover:text-accent-foreground`
})

/**
 * 获取图标样式类
 */
const iconClasses = computed(() => {
  if (isActive.value) {
    return 'h-4 w-4 text-primary'
  }
  return 'h-4 w-4 text-muted-foreground group-hover:text-accent-foreground'
})

/**
 * 获取展开/收起图标样式类
 */
const expandIconClasses = computed(() => {
  if (isActive.value || hasActiveChild.value) {
    return 'h-3.5 w-3.5 text-primary'
  }
  return 'h-3.5 w-3.5 text-muted-foreground'
})

/**
 * 处理点击
 */
function handleClick(): void {
  if (hasChildren.value && !props.collapsed) {
    isExpanded.value = !isExpanded.value
  } else {
    emit('navigate', props.item.path)
  }
}

/**
 * 处理子菜单导航
 */
function handleChildNavigate(path: string): void {
  emit('navigate', path)
}

/**
 * 菜单标题
 */
const menuTitle = computed(() => props.item.title)
</script>

<template>
  <div class="relative">
    <!-- 菜单项 -->
    <Button
      :variant="isActive || hasActiveChild ? 'default' : 'ghost'"
      role="menuitem"
      :aria-expanded="hasChildren ? isExpanded : undefined"
      :aria-haspopup="hasChildren ? true : undefined"
      :aria-current="isActive ? 'page' : undefined"
      :disabled="item.disabled"
      :class="buttonClasses"
      @click="handleClick"
    >
      <div class="flex items-center gap-2 flex-1 min-w-0">
        <!-- 图标 -->
        <component
          :is="item.icon"
          v-if="item.icon && isComponent(item.icon)"
          :class="iconClasses"
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
          {{ formatBadge(typeof item.badge === 'number' ? item.badge : parseInt(item.badge) || 0) }}
        </Badge>

        <!-- 展开/收起图标 -->
        <ChevronDown
          v-if="hasChildren && !collapsed && isExpanded"
          :class="expandIconClasses"
          aria-hidden="true"
        />
        <ChevronRight
          v-else-if="hasChildren && !collapsed"
          :class="expandIconClasses"
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
        v-if="hasChildren && isExpanded && !collapsed"
        role="menu"
        :aria-label="`${item.title} 子菜单`"
        class="ml-4 mt-1 space-y-0.5 overflow-hidden border-l border-border/50 pl-3"
      >
        <!-- 递归渲染子菜单项，传递 activeId 支持无限级菜单 -->
        <LayoutSidebarMenuItemRecursive
          v-for="child in item.children"
          :key="child.key"
          :item="child"
          :collapsed="collapsed"
          :level="level + 1"
          :active-id="activeId"
          @navigate="handleChildNavigate"
        />
      </div>
    </Transition>
  </div>
</template>
