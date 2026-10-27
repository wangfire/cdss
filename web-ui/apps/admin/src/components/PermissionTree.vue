<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ChevronDown, ChevronRight } from 'lucide-vue-next'
import { Checkbox } from '@tabtab/ui'
import { collectCodes, type PermissionNode } from '@/constants/permissions'

const props = withDefaults(defineProps<{
  node: PermissionNode
  selected: string[]
  /** 祖先功能码：授予按钮时一并带上，保证页面入口与按钮权限一致。 */
  ancestors?: string[]
  depth?: number
  disabled?: boolean
}>(), {
  ancestors: () => [],
  depth: 0,
  disabled: false,
})

const emit = defineEmits<{
  (e: 'toggle', payload: {
    node: PermissionNode
    checked: boolean
    ancestors: string[]
  }): void
}>()

const { t } = useI18n()
const open = ref(true)

const nodeCodes = computed(() => collectCodes(props.node))
const selectedCount = computed(
  () => nodeCodes.value.filter(code => props.selected.includes(code)).length,
)
const state = computed<boolean | 'indeterminate'>(() => {
  if (props.disabled) return true
  if (nodeCodes.value.length === 0) return false
  if (selectedCount.value === 0) return false
  return selectedCount.value === nodeCodes.value.length ? true : 'indeterminate'
})

function onToggle(value: boolean | 'indeterminate') {
  if (props.disabled) return
  emit('toggle', {
    node: props.node,
    checked: value === true,
    ancestors: props.ancestors,
  })
}

function childAncestors(): string[] {
  return props.node.code ? [...props.ancestors, props.node.code] : props.ancestors
}
</script>

<template>
  <div>
    <div class="flex items-start gap-2 rounded-md px-2 py-1.5 transition-colors hover:bg-muted/40">
      <button
        v-if="node.children?.length"
        type="button"
        class="mt-0.5 grid size-4 shrink-0 cursor-pointer place-items-center rounded text-muted-foreground hover:bg-muted"
        :aria-label="open ? t('account.collapseNode') : t('account.expandNode')"
        @click="open = !open"
      >
        <ChevronDown v-if="open" class="size-3.5" />
        <ChevronRight v-else class="size-3.5" />
      </button>
      <span v-else class="size-4 shrink-0" />
      <Checkbox
        class="mt-0.5 shrink-0"
        :model-value="state"
        :disabled="disabled"
        @update:model-value="onToggle"
      />
      <div class="min-w-0 flex-1">
        <div class="flex flex-wrap items-center gap-x-2 gap-y-0.5">
          <span
            class="text-sm"
            :class="depth === 0 ? 'font-semibold' : node.children?.length ? 'font-medium' : ''"
          >
            {{ t(node.labelKey) }}
          </span>
          <code
            v-if="node.code && !node.children?.length"
            class="rounded bg-muted px-1 py-px text-[10px] text-muted-foreground"
          >
            {{ node.code }}
          </code>
          <span
            v-if="node.children?.length"
            class="text-[10px] tabular-nums text-muted-foreground"
          >
            {{ selectedCount }}/{{ nodeCodes.length }}
          </span>
        </div>
        <p v-if="node.descKey" class="mt-0.5 text-[11px] leading-4 text-muted-foreground">
          {{ t(node.descKey) }}
        </p>
      </div>
    </div>
    <div
      v-if="open && node.children?.length"
      class="ml-4 border-l border-border/60 pl-2"
    >
      <PermissionTree
        v-for="child in node.children"
        :key="child.key"
        :node="child"
        :selected="selected"
        :ancestors="childAncestors()"
        :depth="depth + 1"
        :disabled="disabled"
        @toggle="(payload) => emit('toggle', payload)"
      />
    </div>
  </div>
</template>
