<script setup lang="ts">
import type { MenuItem } from '@tabtab/ui'
import { LayoutSearch, Button } from '@tabtab/ui'
import { Github } from 'lucide-vue-next'
import Notification from '@/components/Notification.vue'
import LanguageSwitch from '@/components/LanguageSwitch.vue'
import ThemeSwitcher from '@/components/ThemeSwitcher.vue'
import ThemeSettings from '@/components/ThemeSettings.vue'

defineProps<{
  visibleMenuItems: MenuItem[]
  placeholder?: string
  description?: string
  emptyText?: string
  groupTitles?: {
    recent?: string
    navigation?: string
    actions?: string
  }
  showThemeSettings?: boolean
}>()

const showThemeSettings = defineModel<boolean>('showThemeSettings', { default: true })
</script>

<template>
  <div class="flex items-center gap-2">
    <Button variant="ghost" size="icon" as-child>
      <a href="https://github.com/tabtab-dev/ui" target="_blank" rel="noopener noreferrer">
        <Github :size="20" class="shrink-0" />
      </a>
    </Button>
    <LayoutSearch
      :menu-items="visibleMenuItems"
      :placeholder="placeholder || 'Search...'"
      :description="description"
      :empty-text="emptyText"
      :group-titles="groupTitles"
    />
    <Notification />
    <LanguageSwitch mode="dropdown" size="icon" variant="ghost" />
    <ThemeSwitcher />
    <ThemeSettings v-if="showThemeSettings" />
  </div>
</template>
