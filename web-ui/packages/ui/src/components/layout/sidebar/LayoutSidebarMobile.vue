<script setup lang="ts">
import type { SidebarConfig } from './config'
import { computed } from 'vue'
import { X } from 'lucide-vue-next'
import { Sheet, SheetContent, SheetHeader, SheetTitle } from '../../ui/sheet'
import { Button } from '../../ui/button'
import { ScrollArea } from '../../ui/scroll-area'
import { useMenuUtils } from '../composables'
import LayoutSidebarItem from './LayoutSidebarItem.vue'
import LayoutSidebarSubMenu from './LayoutSidebarSubMenu.vue'

/**
 * LayoutSidebarMobile - 移动端侧边栏组件
 * 使用 Sheet 组件实现抽屉式侧边栏
 */

interface Props {
  /** 侧栏配置 */
  config: SidebarConfig
  /** 展开的子菜单 keys */
  expandedKeys: Set<string>
  /** 是否打开 */
  open?: boolean
}

const props = defineProps<Props>()

const emit = defineEmits<{
  /** 更新打开状态 */
  (e: 'update:open', value: boolean): void
  /** 切换子菜单 */
  (e: 'toggleSubMenu', key: string): void
  /** 导航 */
  (e: 'navigate', path: string): void
}>()

/**
 * 使用菜单工具函数
 */
const { isActive, isExpanded: checkExpanded } = useMenuUtils({
  expandedKeys: computed(() => props.expandedKeys),
})

/**
 * 判断是否展开
 */
function isExpanded(key: string): boolean {
  return checkExpanded(key)
}

/**
 * 处理导航
 */
function handleNavigate(path: string): void {
  emit('navigate', path)
  emit('update:open', false)
}

/**
 * 切换子菜单
 */
function handleToggleSubMenu(key: string): void {
  emit('toggleSubMenu', key)
}

/**
 * 判断是否有子菜单
 */
function hasChildren(item: { children?: unknown[] }): boolean {
  return !!item.children && item.children.length > 0
}

/**
 * 打开状态
 */
const openModel = computed({
  get: () => props.open ?? false,
  set: (value) => emit('update:open', value),
})
</script>

<template>
  <Sheet v-model:open="openModel">
    <SheetContent side="left" class="w-[280px] p-0">
      <!-- 头部 -->
      <SheetHeader class="border-b border-border/30 p-4">
        <div class="flex items-center gap-3">
          <slot name="logo">
            <div class="h-10 w-10 rounded-lg bg-primary/10 flex items-center justify-center">
              <span class="text-sm font-bold text-primary">T</span>
            </div>
          </slot>
          <div class="flex flex-col min-w-0">
            <SheetTitle class="text-sm font-bold tracking-tight truncate">
              TabTab Admin
            </SheetTitle>
            <span class="text-[10px] text-muted-foreground truncate">管理系统</span>
          </div>
        </div>
      </SheetHeader>

      <!-- 菜单列表 -->
      <ScrollArea class="flex-1 h-[calc(100vh-140px)]">
        <nav class="p-3 space-y-1">
          <template v-for="item in config.menus" :key="item.key">
            <LayoutSidebarSubMenu
              v-if="hasChildren(item)"
              :item="item"
              :collapsed="false"
              :active="isActive(item.path)"
              :expanded="isExpanded(item.key)"
              @toggle="handleToggleSubMenu(item.key)"
              @navigate="handleNavigate"
            />
            <LayoutSidebarItem
              v-else
              :item="item"
              :title="item.title"
              :collapsed="false"
              :active="isActive(item.path)"
              @navigate="handleNavigate"
            />
          </template>
        </nav>
      </ScrollArea>

      <!-- 底部 -->
      <div class="border-t border-border/30 p-4">
        <slot name="footer">
          <div class="flex items-center gap-3">
            <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
              <span class="text-sm font-semibold text-primary">U</span>
            </div>
            <div class="flex flex-col min-w-0 flex-1">
              <span class="text-sm font-medium truncate">用户</span>
              <span class="text-[11px] text-muted-foreground truncate">user@example.com</span>
            </div>
          </div>
        </slot>
      </div>
    </SheetContent>
  </Sheet>
</template>
