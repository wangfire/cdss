<script setup lang="ts">
import type { SplitterPanel } from 'reka-ui'
import type { SidebarConfig, LayoutSidebarMenuItem } from './config'
import { computed, ref } from 'vue'
import { ChevronUp, LogOut, PanelLeft, PanelRight, Settings, User } from 'lucide-vue-next'
import { Avatar, AvatarFallback } from '../../ui/avatar'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '../../ui/dropdown-menu'
import { ResizableHandle, ResizablePanel, ResizablePanelGroup } from '../../ui/resizable'
import { ScrollArea } from '../../ui/scroll-area'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '../../ui/tooltip'
import { pxToPercent, useMenuUtils } from '../composables'
import LayoutSidebarItem from './LayoutSidebarItem.vue'
import LayoutSidebarSubMenu from './LayoutSidebarSubMenu.vue'

/**
 * LayoutSidebarDesktop - 桌面端侧边栏组件
 * 使用 ResizablePanelGroup 实现可调整宽度
 */

interface Props {
  /** 侧栏配置 */
  config: SidebarConfig
  /** 是否折叠 */
  collapsed: boolean
  /** 当前尺寸（百分比） */
  currentSize: number
  /** 是否拖拽中 */
  isDragging: boolean
  /** 展开的子菜单 keys */
  expandedKeys: Set<string>
}

const props = defineProps<Props>()

const emit = defineEmits<{
  /** 调整尺寸 */
  (e: 'resize', size: number): void
  /** 拖拽状态变化 */
  (e: 'dragging', dragging: boolean): void
  /** 切换子菜单 */
  (e: 'toggleSubMenu', key: string): void
  /** 导航 */
  (e: 'navigate', path: string): void
  /** 切换折叠状态 */
  (e: 'toggleCollapse'): void
}>()

/**
 * 侧边栏面板 ref
 */
const sidebarPanelRef = ref<InstanceType<typeof SplitterPanel> | null>(null)

/**
 * 面板大小（百分比）
 */
const panelSize = computed(() => pxToPercent(props.config.defaultWidth, window.innerWidth))

/**
 * 最小尺寸（百分比）
 */
const minSizePercent = computed(() => pxToPercent(props.config.minWidth, window.innerWidth))

/**
 * 最大尺寸（百分比）
 */
const maxSizePercent = computed(() => pxToPercent(props.config.maxWidth, window.innerWidth))

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
function hasChildren(item: LayoutSidebarMenuItem): boolean {
  return !!item.children && item.children.length > 0
}

/**
 * 处理折叠切换
 */
function handleToggleCollapse(): void {
  emit('toggleCollapse')
}

/**
 * 用户菜单打开状态
 */
const isUserMenuOpen = ref(false)

/**
 * 用户姓名首字母
 */
const userInitials = computed(() => 'U')

/**
 * 处理导航到个人资料
 */
function handleGoToProfile(): void {
  emit('navigate', '/profile')
}

/**
 * 处理导航到设置
 */
function handleGoToSettings(): void {
  emit('navigate', '/settings')
}

/**
 * 处理退出登录
 */
function handleLogout(): void {
  emit('navigate', '/login')
}
</script>

<template>
  <ResizablePanelGroup direction="horizontal" class="h-full hidden lg:flex">
    <!-- 侧栏面板 -->
    <ResizablePanel
      ref="sidebarPanelRef"
      :min-size="minSizePercent"
      :max-size="maxSizePercent"
      :default-size="panelSize"
      class="flex flex-col border-r border-border/30 bg-background/80 backdrop-blur-xl supports-[backdrop-filter]:bg-background/70 relative"
      :class="{ 'transition-none': isDragging }"
      :style="collapsed ? { flex: `0 0 ${config.collapsedWidth}px` } : {}"
      @resize="(size: number) => $emit('resize', size)"
    >
      <!-- 菜单区域背景装饰 -->
      <div v-if="!collapsed" class="absolute inset-0 pointer-events-none">
        <div
          class="absolute top-0 left-0 right-0 h-32 bg-gradient-to-b from-primary/3 to-transparent"
        />
        <div
          class="absolute bottom-0 left-0 right-0 h-32 bg-gradient-to-t from-muted/20 to-transparent"
        />
      </div>

      <!-- Logo 区域 -->
      <div class="relative z-10 border-b border-border/30" :class="collapsed ? 'p-2' : 'p-3'">
        <div
          class="flex items-center transition-all duration-200"
          :class="collapsed ? 'justify-center' : 'gap-3'"
        >
          <slot name="logo">
            <div class="h-14 w-14 rounded-lg bg-primary/10 flex items-center justify-center">
              <span class="text-lg font-bold text-primary">T</span>
            </div>
          </slot>
          <div v-if="!collapsed" class="flex flex-col min-w-0">
            <span class="text-sm font-bold tracking-tight truncate">TabTab Admin</span>
            <span class="text-[10px] text-muted-foreground truncate">管理系统</span>
          </div>
        </div>

        <!-- 折叠按钮 -->
        <button
          v-if="!collapsed"
          class="mt-2 w-full h-8 flex items-center justify-center rounded-lg bg-muted/50 hover:bg-muted hover:text-primary transition-all duration-200 gap-2"
          @click="handleToggleCollapse"
        >
          <PanelLeft class="h-4 w-4" />
          <span class="text-xs text-muted-foreground">收起侧栏</span>
        </button>

        <TooltipProvider v-else>
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
              <span>展开侧栏</span>
            </TooltipContent>
          </Tooltip>
        </TooltipProvider>
      </div>

      <!-- 菜单列表 -->
      <ScrollArea class="flex-1 h-0 relative z-10">
        <nav class="p-3 space-y-1 relative z-10">
          <template v-for="item in config.menus" :key="item.key">
            <!-- 展开状态 -->
            <template v-if="!collapsed">
              <LayoutSidebarSubMenu
                v-if="hasChildren(item)"
                :item="item"
                :collapsed="collapsed"
                :active="isActive(item.path)"
                :expanded="isExpanded(item.key)"
                @toggle="handleToggleSubMenu(item.key)"
                @navigate="handleNavigate"
              />
              <LayoutSidebarItem
                v-else
                :item="item"
                :title="item.title"
                :collapsed="collapsed"
                :active="isActive(item.path)"
                @navigate="handleNavigate"
              />
            </template>

            <!-- 折叠状态 -->
            <template v-else>
              <LayoutSidebarSubMenu
                v-if="hasChildren(item)"
                :item="item"
                :collapsed="collapsed"
                :active="isActive(item.path)"
                :expanded="isExpanded(item.key)"
                @toggle="handleToggleSubMenu(item.key)"
                @navigate="handleNavigate"
              />
              <LayoutSidebarItem
                v-else
                :item="item"
                :title="item.title"
                :collapsed="collapsed"
                :active="isActive(item.path)"
                @navigate="handleNavigate"
              />
            </template>
          </template>
        </nav>
      </ScrollArea>

      <!-- 底部区域 -->
      <TooltipProvider>
        <div class="relative flex-shrink-0 min-h-[70px]">
          <div
            class="absolute top-0 left-4 right-4 h-px bg-gradient-to-r from-transparent via-border to-transparent"
          />

          <div class="border-t border-border/30 bg-muted/40 backdrop-blur-md pt-1">
            <slot name="footer">
              <!-- 展开状态 -->
              <div v-if="!collapsed" class="px-3 py-2">
                <DropdownMenu v-model:open="isUserMenuOpen">
                  <DropdownMenuTrigger as-child>
                    <button
                      class="group flex items-center gap-2.5 min-w-0 w-full rounded-xl p-2 hover:bg-muted/60 transition-all duration-200"
                    >
                      <div class="relative flex-shrink-0">
                        <Avatar
                          class="h-9 w-9 ring-2 ring-primary/20 transition-all duration-200 group-hover:ring-primary/40 group-hover:shadow-md"
                        >
                          <AvatarFallback
                            class="text-sm font-semibold bg-gradient-to-br from-primary to-primary/70 text-primary-foreground"
                          >
                            {{ userInitials }}
                          </AvatarFallback>
                        </Avatar>
                        <span
                          class="absolute -bottom-0.5 -right-0.5 h-3 w-3 rounded-full bg-emerald-500 ring-2 ring-background"
                        />
                      </div>
                      <div class="flex flex-col min-w-0 flex-1 text-left">
                        <span
                          class="text-sm font-medium truncate group-hover:text-primary transition-colors duration-200"
                        >
                          用户
                        </span>
                        <span class="text-[11px] text-muted-foreground truncate">
                          user@example.com
                        </span>
                      </div>
                      <ChevronUp
                        class="h-4 w-4 text-muted-foreground flex-shrink-0 transition-transform duration-200 group-data-[state=open]:rotate-180"
                      />
                    </button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="start" class="w-56" :side-offset="8">
                    <div class="px-2 py-1.5">
                      <p class="text-xs font-medium text-muted-foreground">已登录</p>
                      <p class="text-sm font-semibold truncate">user@example.com</p>
                    </div>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem class="gap-2 cursor-pointer" @click="handleGoToProfile">
                      <User class="h-4 w-4" />
                      <span>个人资料</span>
                    </DropdownMenuItem>
                    <DropdownMenuItem class="gap-2 cursor-pointer" @click="handleGoToSettings">
                      <Settings class="h-4 w-4" />
                      <span>设置</span>
                    </DropdownMenuItem>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem
                      class="gap-2 cursor-pointer text-destructive focus:text-destructive"
                      @click="handleLogout"
                    >
                      <LogOut class="h-4 w-4" />
                      <span>退出登录</span>
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </div>

              <!-- 折叠状态：用户头像 -->
              <div v-else class="py-3 px-2 flex flex-col items-center">
                <DropdownMenu v-model:open="isUserMenuOpen">
                  <DropdownMenuTrigger as-child>
                    <button class="group relative">
                      <Avatar
                        class="h-10 w-10 ring-2 ring-primary/20 transition-all duration-200 group-hover:ring-primary/50 group-hover:shadow-lg"
                      >
                        <AvatarFallback
                          class="text-sm font-semibold bg-gradient-to-br from-primary to-primary/80 text-primary-foreground"
                        >
                          {{ userInitials }}
                        </AvatarFallback>
                      </Avatar>
                      <span
                        class="absolute -bottom-0.5 -right-0.5 h-3 w-3 rounded-full bg-emerald-500 ring-2 ring-background"
                      />
                    </button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="start" side="right" class="w-56" :side-offset="8">
                    <div class="px-2 py-1.5">
                      <p class="text-xs font-medium text-muted-foreground">已登录</p>
                      <p class="text-sm font-semibold truncate">user@example.com</p>
                    </div>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem class="gap-2 cursor-pointer" @click="handleGoToProfile">
                      <User class="h-4 w-4" />
                      <span>个人资料</span>
                    </DropdownMenuItem>
                    <DropdownMenuItem class="gap-2 cursor-pointer" @click="handleGoToSettings">
                      <Settings class="h-4 w-4" />
                      <span>设置</span>
                    </DropdownMenuItem>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem
                      class="gap-2 cursor-pointer text-destructive focus:text-destructive"
                      @click="handleLogout"
                    >
                      <LogOut class="h-4 w-4" />
                      <span>退出登录</span>
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </div>
            </slot>
          </div>
        </div>
      </TooltipProvider>
    </ResizablePanel>

    <!-- 拖拽手柄 -->
    <ResizableHandle
      v-if="!collapsed"
      with-handle
      class="w-1.5 bg-transparent hover:bg-primary/20 transition-colors relative after:absolute after:inset-y-4 after:left-1/2 after:-translate-x-1/2 after:w-1 after:h-8 after:rounded-full after:bg-border/60 hover:after:bg-primary/50"
      @dragging="(dragging: boolean) => $emit('dragging', dragging)"
    />

    <!-- 内容面板 -->
    <ResizablePanel :min-size="50">
      <main class="h-full min-w-0 bg-background">
        <slot />
      </main>
    </ResizablePanel>
  </ResizablePanelGroup>
</template>
