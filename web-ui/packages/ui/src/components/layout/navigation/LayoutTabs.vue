<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { TabItem } from './LayoutTabsItem.vue'
import { cn } from '@tabtab/utils'
import { ref, computed, onMounted, onUnmounted, nextTick, watch } from 'vue'
import { useResizeObserver, useScroll } from '@vueuse/core'
import {
  X,
  Pin,
  PinOff,
  ChevronsRight,
  ChevronsLeft,
  ChevronLeft,
  ChevronRight,
  Ellipsis,
  RefreshCw,
} from 'lucide-vue-next'
import { Button } from '../../ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '../../ui/dropdown-menu'
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from '../../ui/context-menu'
import LayoutTabsItem from './LayoutTabsItem.vue'

interface Props {
  tabs: TabItem[]
  activeKey?: string
  fixed?: boolean
  class?: HTMLAttributes['class']
}

const props = withDefaults(defineProps<Props>(), {
  activeKey: '',
  fixed: true,
})

const emit = defineEmits<{
  'update:activeKey': [key: string]
  close: [key: string]
  closeOther: [key: string]
  closeLeft: [key: string]
  closeRight: [key: string]
  closeAll: []
  toggleAffix: [key: string]
  refresh: [key: string]
  reorder: [fromKey: string, toKey: string]
}>()

const scrollContainerRef = ref<HTMLElement>()
const tabsContainerRef = ref<HTMLElement>()

/**
 * 溢出状态
 */
const isOverflowing = ref(false)
const isAtStart = ref(true)
const isAtEnd = ref(true)

/**
 * 使用 VueUse 的 useScroll
 */
const { x: scrollX } = useScroll(tabsContainerRef, {
  behavior: 'smooth',
})

/**
 * 右键菜单状态
 */
const contextMenuTabKey = ref('')

/**
 * 当前右键选中的标签
 */
const contextMenuTab = computed(() => props.tabs.find((tab) => tab.key === contextMenuTabKey.value))

/**
 * 当前右键标签的索引
 */
const contextMenuTabIndex = computed(() =>
  props.tabs.findIndex((tab) => tab.key === contextMenuTabKey.value),
)

const defaultMenuText = computed(() => {
  const firstTab = props.tabs[0]
  return firstTab?.menuText || {}
})

/**
 * 是否可以滚动
 */
const canScrollLeft = computed(() => isOverflowing.value && !isAtStart.value)
const canScrollRight = computed(() => isOverflowing.value && !isAtEnd.value)

/**
 * 检查溢出和滚动位置
 */
function checkOverflow() {
  nextTick(() => {
    const container = scrollContainerRef.value
    const content = tabsContainerRef.value
    if (!container || !content) {
      isOverflowing.value = false
      isAtStart.value = true
      isAtEnd.value = true
      return
    }
    isOverflowing.value = content.scrollWidth > container.clientWidth

    const maxScroll = content.scrollWidth - container.clientWidth
    const currentScroll = content.scrollLeft
    isAtStart.value = currentScroll <= 0
    isAtEnd.value = currentScroll >= maxScroll - 1
  })
}

/**
 * 使用 resize observer 监听容器变化
 */
useResizeObserver(scrollContainerRef, checkOverflow)
useResizeObserver(tabsContainerRef, checkOverflow)

/**
 * 监听标签数量变化
 * 并在动画结束后再次检测（TransitionGroup 动画持续 200ms）
 */
watch(
  () => props.tabs.length,
  () => {
    checkOverflow()
    setTimeout(checkOverflow, 250)
  },
  { immediate: true },
)

/**
 * 监听滚动位置变化
 */
watch(scrollX, checkOverflow)

/**
 * 滚动到指定标签
 */
function scrollToTab(key: string) {
  nextTick(() => {
    const tabElement = tabsContainerRef.value?.querySelector(
      `[data-tab-key="${key}"]`,
    ) as HTMLElement
    if (tabElement && tabsContainerRef.value && scrollContainerRef.value) {
      const containerRect = scrollContainerRef.value.getBoundingClientRect()
      const tabRect = tabElement.getBoundingClientRect()
      const currentScroll = tabsContainerRef.value.scrollLeft

      const tabCenter = tabRect.left + tabRect.width / 2 - containerRect.left
      const containerCenter = containerRect.width / 2
      const newScroll = currentScroll + tabCenter - containerCenter

      tabsContainerRef.value.scrollTo({
        left: newScroll,
        behavior: 'smooth',
      })
    }
  })
}

/**
 * 滚动偏移量
 */
const scrollOffset = 200

/**
 * 向左滚动
 */
function scrollLeft() {
  tabsContainerRef.value?.scrollBy({
    left: -scrollOffset,
    behavior: 'smooth',
  })
}

/**
 * 向右滚动
 */
function scrollRight() {
  tabsContainerRef.value?.scrollBy({
    left: scrollOffset,
    behavior: 'smooth',
  })
}

/**
 * 滚动到开始
 */
function scrollToStart() {
  tabsContainerRef.value?.scrollTo({
    left: 0,
    behavior: 'smooth',
  })
}

/**
 * 滚动到结束
 */
function scrollToEnd() {
  if (tabsContainerRef.value) {
    tabsContainerRef.value.scrollTo({
      left: tabsContainerRef.value.scrollWidth,
      behavior: 'smooth',
    })
  }
}

/**
 * 处理标签点击
 */
function handleTabClick(key: string) {
  emit('update:activeKey', key)
}

/**
 * 处理标签关闭
 */
function handleTabClose(key: string) {
  emit('close', key)
}

/**
 * 关闭其他标签
 */
function handleCloseOther() {
  emit('closeOther', contextMenuTabKey.value)
}

/**
 * 关闭左侧标签
 */
function handleCloseLeft() {
  emit('closeLeft', contextMenuTabKey.value)
}

/**
 * 关闭右侧标签
 */
function handleCloseRight() {
  emit('closeRight', contextMenuTabKey.value)
}

/**
 * 关闭所有标签
 */
function handleCloseAll() {
  emit('closeAll')
}

/**
 * 刷新标签
 */
function handleRefresh() {
  emit('refresh', contextMenuTabKey.value)
}

/**
 * 切换固定状态
 */
function handleToggleAffix() {
  emit('toggleAffix', contextMenuTabKey.value)
}

/**
 * 拖拽状态
 */
const draggedKey = ref<string | null>(null)

/**
 * 是否正在拖拽中
 */
const isDragging = ref(false)

/**
 * 上一次 reorder 的时间戳，用于防抖
 */
let lastReorderTime = 0
const REORDER_DEBOUNCE_MS = 50

/**
 * 处理拖拽开始
 */
function handleDragStart(event: DragEvent, key: string) {
  draggedKey.value = key
  isDragging.value = true
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move'
    event.dataTransfer.setData('text/plain', key)
  }
}

/**
 * 处理拖拽悬停
 */
function handleDragOver(event: DragEvent, targetKey: string) {
  event.preventDefault()
  if (!draggedKey.value || draggedKey.value === targetKey) return

  // 防抖处理，避免频繁触发 reorder
  const now = Date.now()
  if (now - lastReorderTime < REORDER_DEBOUNCE_MS) return
  lastReorderTime = now

  emit('reorder', draggedKey.value, targetKey)
}

/**
 * 处理拖拽结束
 */
function handleDragEnd() {
  draggedKey.value = null
  isDragging.value = false
  lastReorderTime = 0
}

/**
 * 处理鼠标滚轮
 */
function handleWheel(e: WheelEvent) {
  const viewport = tabsContainerRef.value
  if (viewport) {
    viewport.scrollBy({ left: e.deltaY, behavior: 'instant' })
  }
}

/**
 * 监听激活标签变化，自动滚动到可视区域
 */
watch(
  () => props.activeKey,
  (newKey) => {
    if (newKey) {
      scrollToTab(newKey)
    }
  },
  { immediate: true },
)

onMounted(() => {
  window.addEventListener('resize', checkOverflow)
  setTimeout(checkOverflow, 100)
})

onUnmounted(() => {
  window.removeEventListener('resize', checkOverflow)
})
</script>

<template>
  <div
    data-slot="layout-tabs"
    :class="
      cn(
        'relative flex items-center w-full min-w-0 h-(--layout-tabs-height) bg-background/50 supports-[backdrop-filter]:bg-background/50 border-b border-border/20 px-1 gap-0.5',
        props.class,
      )
    "
  >
    <div v-show="isOverflowing" class="flex items-center gap-0.5 flex-shrink-0 relative z-10">
      <Button
        variant="ghost"
        size="icon"
        class="h-7 w-7"
        :disabled="!canScrollLeft"
        @click="scrollToStart"
      >
        <ChevronsLeft class="h-4 w-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        class="h-7 w-7"
        :disabled="!canScrollLeft"
        @click="scrollLeft"
      >
        <ChevronLeft class="h-4 w-4" />
      </Button>
    </div>

    <div ref="scrollContainerRef" class="flex-1 min-w-0 h-full overflow-hidden relative z-10">
      <div
        ref="tabsContainerRef"
        class="flex items-center gap-1 h-full overflow-x-auto overflow-y-hidden"
        style="scrollbar-width: none; -ms-overflow-style: none"
        @wheel.passive="handleWheel"
      >
        <TransitionGroup
          name="tab"
          tag="div"
          class="flex items-center gap-1"
          :class="{ dragging: isDragging }"
        >
          <div v-for="tab in tabs" :key="tab.key" class="flex-shrink-0">
            <ContextMenu
              @update:open="
                (open: boolean) => {
                  if (open) contextMenuTabKey = tab.key
                }
              "
            >
              <ContextMenuTrigger as-child>
                <LayoutTabsItem
                  :tab="tab"
                  :active="activeKey === tab.key"
                  :data-tab-key="tab.key"
                  @click="handleTabClick"
                  @close="handleTabClose"
                  @dragstart="(e, key) => handleDragStart(e, key)"
                  @dragover="(e, key) => handleDragOver(e, key)"
                  @dragend="handleDragEnd"
                />
              </ContextMenuTrigger>
              <ContextMenuContent class="w-48">
                <ContextMenuItem @click="handleRefresh">
                  <RefreshCw class="h-4 w-4 mr-2" />
                  <span>{{ contextMenuTab?.menuText?.refresh || '刷新页面' }}</span>
                </ContextMenuItem>
                <ContextMenuSeparator />
                <ContextMenuItem @click="handleToggleAffix">
                  <Pin v-if="!contextMenuTab?.affix" class="h-4 w-4 mr-2" />
                  <PinOff v-else class="h-4 w-4 mr-2" />
                  <span>{{
                    contextMenuTab?.affix
                      ? contextMenuTab?.menuText?.unpin || '取消固定'
                      : contextMenuTab?.menuText?.pin || '固定标签'
                  }}</span>
                </ContextMenuItem>
                <ContextMenuSeparator />
                <ContextMenuItem
                  v-if="!contextMenuTab?.affix"
                  @click="handleTabClose(contextMenuTabKey)"
                >
                  <X class="h-4 w-4 mr-2" />
                  <span>{{ contextMenuTab?.menuText?.close || '关闭标签' }}</span>
                </ContextMenuItem>
                <ContextMenuItem @click="handleCloseOther">
                  <span>{{ contextMenuTab?.menuText?.closeOthers || '关闭其他' }}</span>
                </ContextMenuItem>
                <ContextMenuItem :disabled="contextMenuTabIndex === 0" @click="handleCloseLeft">
                  <span>{{ contextMenuTab?.menuText?.closeLeft || '关闭左侧' }}</span>
                </ContextMenuItem>
                <ContextMenuItem
                  :disabled="contextMenuTabIndex === tabs.length - 1"
                  @click="handleCloseRight"
                >
                  <span>{{ contextMenuTab?.menuText?.closeRight || '关闭右侧' }}</span>
                </ContextMenuItem>
                <ContextMenuSeparator />
                <ContextMenuItem @click="handleCloseAll">
                  <span>{{ contextMenuTab?.menuText?.closeAll || '关闭所有' }}</span>
                </ContextMenuItem>
              </ContextMenuContent>
            </ContextMenu>
          </div>
        </TransitionGroup>
      </div>
    </div>

    <div v-show="isOverflowing" class="flex items-center gap-0.5 flex-shrink-0 relative z-10">
      <Button
        variant="ghost"
        size="icon"
        class="h-7 w-7"
        :disabled="!canScrollRight"
        @click="scrollRight"
      >
        <ChevronRight class="h-4 w-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        class="h-7 w-7"
        :disabled="!canScrollRight"
        @click="scrollToEnd"
      >
        <ChevronsRight class="h-4 w-4" />
      </Button>
    </div>

    <DropdownMenu>
      <DropdownMenuTrigger as-child>
        <Button variant="ghost" size="icon" class="h-7 w-7 flex-shrink-0 relative z-10">
          <Ellipsis class="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" class="w-48">
        <DropdownMenuItem @click="handleCloseAll">
          <X class="h-4 w-4 mr-2" />
          <span>{{ defaultMenuText.closeAll || '关闭所有' }}</span>
        </DropdownMenuItem>
        <template v-if="isOverflowing">
          <DropdownMenuSeparator />
          <DropdownMenuItem @click="scrollToStart">
            <ChevronsLeft class="h-4 w-4 mr-2" />
            <span>{{ defaultMenuText.scrollToStart || '滚动到开头' }}</span>
          </DropdownMenuItem>
          <DropdownMenuItem @click="scrollToEnd">
            <ChevronsRight class="h-4 w-4 mr-2" />
            <span>{{ defaultMenuText.scrollToEnd || '滚动到末尾' }}</span>
          </DropdownMenuItem>
        </template>
      </DropdownMenuContent>
    </DropdownMenu>
  </div>
</template>

<style scoped>
.overflow-x-auto::-webkit-scrollbar {
  display: none !important;
  width: 0 !important;
  height: 0 !important;
}

.tab-enter-active,
.tab-leave-active {
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}

.tab-enter-from {
  opacity: 0;
  transform: translateX(-10px) scale(0.95);
}

.tab-leave-to {
  opacity: 0;
  transform: translateX(10px) scale(0.95);
}

.tab-move {
  transition: transform 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}

/* 拖拽时禁用所有过渡动画，防止闪烁 */
.dragging .tab-enter-active,
.dragging .tab-leave-active,
.dragging .tab-move {
  transition: none !important;
}

@media (prefers-reduced-motion: reduce) {
  .tab-enter-active,
  .tab-leave-active,
  .tab-move {
    transition: none;
  }
}
</style>
