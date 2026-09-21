<script setup lang="ts">
import type { MenuItem, LayoutSearchProps } from '../types'
import { computed, ref, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useMagicKeys, whenever, useEventListener } from '@vueuse/core'
import { Search } from 'lucide-vue-next'
import {
  CommandDialog,
  CommandInput,
  CommandEmpty,
  CommandGroup,
  CommandItem,
  CommandSeparator,
} from '../../ui/command'
import { ScrollArea } from '../../ui/scroll-area'

const props = withDefaults(defineProps<LayoutSearchProps>(), {
  placeholder: 'Search menu...',
  description: 'Search for menu or features',
  shortcutKey: 'k',
  shortcutModifiers: () => ['ctrl', 'meta'],
  menuItems: () => [],
  recentItems: () => [],
  maxRecentItems: 5,
  showRecent: true,
  groupTitles: () => ({
    recent: 'Recent Access',
    navigation: 'Navigation',
    actions: 'Actions',
  }),
  emptyText: 'No results found',
})

const emit = defineEmits<{
  select: [item: MenuItem]
  open: []
  close: []
}>()

const router = useRouter()
const open = ref(false)
const isExpanded = ref(false)
const searchValue = ref('')
const inputRef = ref<HTMLInputElement | null>(null)

const { ctrl_k, meta_k } = useMagicKeys({
  passive: false,
  onEventFired(e) {
    if (e.key === 'k' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault()
    }
  },
})

const stopCtrlK = whenever(ctrl_k, openSearchDialog)
const stopMetaK = whenever(meta_k, openSearchDialog)

/**
 * 处理失焦事件
 */
function handleBlur() {
  setTimeout(() => {
    if (!searchValue.value) {
      isExpanded.value = false
    }
  }, 150)
}

/**
 * 打开搜索弹框
 */
function openSearchDialog() {
  open.value = true
  emit('open')
}

/**
 * 收缩输入框
 */
function collapse() {
  isExpanded.value = false
  searchValue.value = ''
}

/**
 * 处理键盘事件
 */
function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    if (open.value) {
      open.value = false
    } else if (isExpanded.value) {
      collapse()
    }
  }
}

/**
 * 使用 useEventListener 替代手动事件监听
 * 自动在组件卸载时清理
 */
useEventListener(document, 'keydown', handleKeydown)

function handleOpenChange(value: boolean) {
  open.value = value
  if (value) {
    emit('open')
  } else {
    emit('close')
  }
}

/**
 * 扁平化菜单项，用于搜索
 */
const flattenedMenuItems = computed(() => {
  const result: (MenuItem & { parentTitle?: string })[] = []

  function flatten(menuItems: MenuItem[], parentTitle?: string) {
    for (const item of menuItems) {
      if (!item.hideInMenu) {
        result.push({ ...item, parentTitle })
      }
      if (item.children?.length) {
        flatten(item.children, item.name)
      }
    }
  }

  flatten(props.menuItems)
  return result
})

/**
 * 处理选择菜单项
 */
function handleSelect(item: MenuItem) {
  emit('select', item)

  if (item.href) {
    window.open(item.href, '_blank')
  } else if (item.path) {
    router.push(item.path)
  }

  open.value = false
}

/**
 * 获取菜单项图标
 */
function getItemIcon(item: MenuItem) {
  return item.icon
}

/**
 * 组件卸载时清理 whenever 监听器
 */
onUnmounted(() => {
  stopCtrlK()
  stopMetaK()
})

defineExpose({
  open: openSearchDialog,
  close: () => {
    open.value = false
    emit('close')
  },
  toggle: openSearchDialog,
  collapse,
})
</script>

<template>
  <div data-slot="layout-search" class="relative">
    <div
      :class="[
        'flex items-center h-9 rounded-lg border border-border/40 bg-background/60 backdrop-blur-md supports-[backdrop-filter]:bg-background/50',
        'transition-all duration-300 ease-out',
        'hover:border-border/60 hover:bg-accent/30 dark:hover:bg-accent/20',
        'focus-within:ring-2 focus-within:ring-ring/40 focus-within:border-ring/50',
        isExpanded ? 'w-60 px-3' : 'w-44 px-3',
      ]"
    >
      <Search class="h-4 w-4 shrink-0 text-muted-foreground" />

      <input
        ref="inputRef"
        v-model="searchValue"
        type="text"
        :placeholder="isExpanded ? placeholder : placeholder"
        class="ml-2 min-w-0 flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
        @focus="isExpanded = true"
        @blur="handleBlur"
        @keydown.enter="openSearchDialog"
        @keydown.escape="collapse"
      />

      <kbd
        class="text-[10px] text-muted-foreground px-1.5 py-0.5 rounded border border-border/40 bg-muted/30 shrink-0"
      >
        ⌘K
      </kbd>
    </div>
  </div>

  <CommandDialog
    v-model:open="open"
    :title="placeholder"
    :description="description"
    class="max-w-lg"
    @update:open="handleOpenChange"
  >
    <CommandInput :placeholder="placeholder" :default-value="searchValue" />
    <ScrollArea class="h-[320px]">
      <CommandEmpty class="py-6 text-center text-sm text-muted-foreground">{{
        emptyText
      }}</CommandEmpty>

      <CommandGroup
        v-if="showRecent && recentItems.length > 0"
        :heading="groupTitles.recent"
        class="p-2"
      >
        <CommandItem
          v-for="item in recentItems.slice(0, maxRecentItems)"
          :key="item.id"
          :value="item.name"
          class="cursor-pointer rounded-lg px-3 py-2 transition-colors data-[highlighted]:bg-accent/50"
          @select="handleSelect(item)"
        >
          <component
            :is="getItemIcon(item)"
            v-if="item.icon"
            class="h-4 w-4 mr-3 text-muted-foreground"
          />
          <span class="text-sm">{{ item.name }}</span>
          <span v-if="item.parentTitle" class="ml-auto text-xs text-muted-foreground">
            {{ item.parentTitle }}
          </span>
        </CommandItem>
      </CommandGroup>

      <CommandSeparator
        v-if="showRecent && recentItems.length > 0 && flattenedMenuItems.length > 0"
        class="my-1"
      />

      <CommandGroup
        v-if="flattenedMenuItems.length > 0"
        :heading="groupTitles.navigation"
        class="p-2"
      >
        <CommandItem
          v-for="item in flattenedMenuItems"
          :key="item.id"
          :value="item.name"
          class="cursor-pointer rounded-lg px-3 py-2 transition-colors data-[highlighted]:bg-accent/50"
          @select="handleSelect(item)"
        >
          <component
            :is="getItemIcon(item)"
            v-if="item.icon"
            class="h-4 w-4 mr-3 text-muted-foreground"
          />
          <span class="text-sm">{{ item.name }}</span>
          <span v-if="item.parentTitle" class="ml-auto text-xs text-muted-foreground">
            {{ item.parentTitle }}
          </span>
        </CommandItem>
      </CommandGroup>

      <slot name="actions" :on-select="handleSelect" />
    </ScrollArea>
  </CommandDialog>
</template>
