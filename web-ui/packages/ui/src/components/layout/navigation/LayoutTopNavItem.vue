<script setup lang="ts">
import type { MenuItem } from '../types'
import { computed } from 'vue'
import { ChevronDown } from 'lucide-vue-next'
import { Button } from '../../ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuTrigger } from '../../ui/dropdown-menu'
import { isComponent } from '../composables'
import LayoutTopNavDropdownItem from './LayoutTopNavDropdownItem.vue'

/**
 * LayoutTopNavItem - 顶部导航菜单项组件
 * 参考单栏侧栏 SidebarItem 的样式风格
 * 支持：
 * - 显示 icon
 * - 单级菜单直接跳转
 * - 多级菜单使用 DropdownMenu 下拉展示
 * - 无限层级嵌套
 */

interface Props {
  /** 菜单项数据 */
  item: MenuItem
  /** 当前激活的菜单项 key */
  activeId?: string
}

const props = defineProps<Props>()

const emit = defineEmits<{
  /** 导航事件 */
  (e: 'navigate', item: MenuItem): void
}>()

/**
 * 是否有子菜单
 */
const hasChildren = computed(() => {
  return !!props.item.children && props.item.children.length > 0
})

/**
 * 检查当前项或其任意子项是否匹配 targetId
 * 递归检查所有层级
 */
function checkSubtree(item: MenuItem, targetId: string): boolean {
  if (item.id === targetId) return true
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
  return props.item.id === props.activeId
})

/**
 * 是否有子项激活（递归检查所有子层级）
 */
const hasActiveChild = computed(() => {
  if (!props.activeId || !props.item.children) return false
  return props.item.children.some((child) => checkSubtree(child, props.activeId!))
})

/**
 * 是否显示为激活状态（自身激活或有子项激活）
 */
const isActiveState = computed(() => {
  return isActive.value || hasActiveChild.value
})

/**
 * 处理点击
 */
function handleClick() {
  emit('navigate', props.item)
}

/**
 * 处理子菜单导航
 */
function handleChildNavigate(item: MenuItem) {
  emit('navigate', item)
}
</script>

<template>
  <!-- 有子菜单：显示下拉菜单 -->
  <DropdownMenu v-if="hasChildren">
    <DropdownMenuTrigger as-child>
      <Button
        variant="ghost"
        size="sm"
        class="relative transition-all duration-200 group"
        :class="[
          isActiveState
            ? 'bg-primary/10 text-primary font-medium hover:bg-primary/15 shadow-sm'
            : 'hover:bg-muted/50',
        ]"
      >
        <component
          :is="item.icon"
          v-if="item.icon && isComponent(item.icon)"
          class="h-4 w-4 mr-1.5"
          :class="[
            isActiveState ? 'text-primary' : 'text-muted-foreground group-hover:text-foreground',
          ]"
        />
        <span
          :class="[
            isActiveState
              ? 'text-primary font-medium'
              : 'text-muted-foreground group-hover:text-foreground',
          ]"
        >
          {{ item.name }}
        </span>
        <ChevronDown
          class="h-3.5 w-3.5 ml-1 transition-transform duration-200"
          :class="[
            isActiveState ? 'text-primary' : 'text-muted-foreground group-hover:text-foreground',
          ]"
        />
        <!-- 底部边框指示器（顶部导航特色） -->
        <span
          v-if="isActiveState"
          class="absolute bottom-0 left-1/2 -translate-x-1/2 w-10 h-[3px] bg-gradient-to-r from-primary/80 via-primary to-primary/80 rounded-full shadow-sm shadow-primary/20"
        />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="start" class="min-w-[180px]">
      <LayoutTopNavDropdownItem
        v-for="child in item.children"
        :key="child.id"
        :item="child"
        :active-id="props.activeId"
        @navigate="handleChildNavigate"
      />
    </DropdownMenuContent>
  </DropdownMenu>

  <!-- 无子菜单：显示普通按钮 -->
  <Button
    v-else
    variant="ghost"
    size="sm"
    class="relative transition-all duration-200 group"
    :class="[
      isActiveState
        ? 'bg-primary/10 text-primary font-medium hover:bg-primary/15 shadow-sm'
        : 'hover:bg-muted/50',
    ]"
    @click="handleClick"
  >
    <component
      :is="item.icon"
      v-if="item.icon && isComponent(item.icon)"
      class="h-4 w-4 mr-1.5"
      :class="[
        isActiveState ? 'text-primary' : 'text-muted-foreground group-hover:text-foreground',
      ]"
    />
    <span
      :class="[
        isActiveState
          ? 'text-primary font-medium'
          : 'text-muted-foreground group-hover:text-foreground',
      ]"
    >
      {{ item.name }}
    </span>
    <!-- 底部边框指示器（顶部导航特色） -->
    <span
      v-if="isActiveState"
      class="absolute bottom-0 left-1/2 -translate-x-1/2 w-10 h-[3px] bg-gradient-to-r from-primary/80 via-primary to-primary/80 rounded-full shadow-sm shadow-primary/20"
    />
  </Button>
</template>
