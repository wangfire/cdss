<script setup lang="ts">
import { Palette, Sun, Moon, Monitor, RotateCcw } from 'lucide-vue-next'
import { ref, computed } from 'vue'
import {
  Button,
  Card,
  CardContent,
  Label,
  ScrollArea,
  Separator,
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
  Slider,
  Switch,
} from '@tabtab/ui'
import { useThemeStore, type ThemeMode, type ThemeColor } from '@/stores/theme'
import type { LayoutMode, LayoutVariant } from '@tabtab/ui'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const themeStore = useThemeStore()
const open = ref(false)

const themeModeOptions = computed(() => [
  { value: 'light' as ThemeMode, label: t('settings.lightMode'), icon: 'sun' },
  { value: 'dark' as ThemeMode, label: t('settings.darkMode'), icon: 'moon' },
  { value: 'system' as ThemeMode, label: t('settings.systemMode'), icon: 'monitor' },
])

const layoutModeOptions = computed(() => [
  {
    value: 'sidebar' as LayoutMode,
    label: t('settings.sidebar'),
    description: t('settings.sidebarDesc'),
    icon: 'sidebar',
  },
  {
    value: 'top-nav' as LayoutMode,
    label: t('settings.topNav'),
    description: t('settings.topNavDesc'),
    icon: 'topnav',
  },
  {
    value: 'mixed' as LayoutMode,
    label: t('settings.mixed'),
    description: t('settings.mixedDesc'),
    icon: 'mixed',
  },
  {
    value: 'double-sidebar' as LayoutMode,
    label: t('settings.doubleSidebar'),
    description: t('settings.doubleSidebarDesc'),
    icon: 'double',
  },
  {
    value: 'mixed-double' as LayoutMode,
    label: t('settings.mixedDouble'),
    description: t('settings.mixedDoubleDesc'),
    icon: 'mixed-double',
  },
])

const radiusValue = computed({
  get: () => [themeStore.borderRadius * 16],
  set: (value: number[]) => {
    themeStore.setBorderRadius(value[0] / 16)
  },
})

const radiusLabel = computed(() => {
  const px = Math.round(themeStore.borderRadius * 16)
  return `${px}px`
})

/**
 * 设置圆角为预设值
 */
function setRadiusPreset(value: number) {
  themeStore.setBorderRadius(value / 16)
}

const radiusPresets = computed(() => [
  { value: 0, label: t('settings.borderRadiusNone') },
  { value: 4, label: t('settings.borderRadiusSmall') },
  { value: 8, label: t('settings.borderRadiusMedium') },
  { value: 12, label: t('settings.borderRadiusLarge') },
  { value: 16, label: t('settings.borderRadiusFull') },
])

/**
 * 侧边栏宽度范围（像素）
 */
const sidebarWidthRange = {
  min: 200,
  max: 400,
  default: 260,
}

/**
 * 侧边栏宽度值
 */
const sidebarWidthValue = computed({
  get: () => [themeStore.sidebarWidth],
  set: (value: number[]) => {
    themeStore.setSidebarWidth(value[0])
  },
})

/**
 * 侧边栏宽度标签
 */
const sidebarWidthLabel = computed(() => {
  return `${themeStore.sidebarWidth}px`
})

/**
 * 是否显示侧边栏宽度设置
 * 只有在 sidebar 或 mixed 模式下才显示
 */
const showSidebarWidthSetting = computed(() => {
  return themeStore.layoutMode === 'sidebar' || themeStore.layoutMode === 'mixed'
})

/**
 * 是否显示侧边栏折叠开关
 * 只有在 sidebar、double-sidebar、mixed-double 模式下才显示
 */
const showSidebarCollapse = computed(() => {
  return ['sidebar', 'double-sidebar', 'mixed-double'].includes(themeStore.layoutMode)
})

/**
 * 内容宽度选项
 */
const contentWidthOptions = computed(() => [
  {
    value: 'streamer' as LayoutVariant,
    label: t('settings.fullWidth'),
    description: t('settings.fullWidthDesc'),
  },
  {
    value: 'fixed' as LayoutVariant,
    label: t('settings.fixed'),
    description: t('settings.fixedDesc'),
  },
])

const themeColorNames = computed(() => {
  return themeStore.availableThemes.map((theme) => ({
    ...theme,
    translatedName: t(`settings.themeColorNames.${theme.key}`) || theme.name,
  }))
})

/**
 * 内容宽度值
 */
const contentWidthValue = computed({
  get: () => themeStore.layoutVariant,
  set: (value: LayoutVariant) => {
    themeStore.setLayoutVariant(value)
  },
})

/**
 * 重置主题设置为默认值
 */
function handleReset() {
  themeStore.resetThemeSettings()
}
</script>

<template>
  <Sheet v-model:open="open">
    <SheetTrigger as-child>
      <Button variant="ghost" size="icon" :aria-label="t('settings.openThemeSettings')">
        <Palette class="h-5 w-5" />
        <span class="sr-only">{{ t('settings.theme') }}</span>
      </Button>
    </SheetTrigger>
    <SheetContent class="w-[360px] sm:w-[400px] p-0 gap-0">
      <SheetHeader class="px-5 pt-6 pb-2">
        <SheetTitle>{{ t('settings.theme') }}</SheetTitle>
        <SheetDescription>{{ t('settings.themeDescription') }}</SheetDescription>
      </SheetHeader>

      <ScrollArea class="h-[calc(100vh-80px)]">
        <div class="space-y-6 px-5 pb-6">
          <!-- 主题模式 -->
          <div class="space-y-3">
            <Label class="text-base font-medium">{{ t('settings.themeMode') }}</Label>
            <div class="grid grid-cols-3 gap-3">
              <Button
                v-for="option in themeModeOptions"
                :key="option.value"
                :variant="themeStore.themeMode === option.value ? 'default' : 'outline'"
                class="flex flex-col items-center gap-1.5 h-auto py-3 transition-all duration-200"
                :aria-label="`Switch to ${option.label}`"
                :aria-pressed="themeStore.themeMode === option.value"
                @click="themeStore.setThemeMode(option.value)"
              >
                <Sun
                  v-if="option.icon === 'sun'"
                  :size="20"
                  :class="
                    themeStore.themeMode === option.value
                      ? 'text-primary-foreground'
                      : 'text-amber-500'
                  "
                />
                <Moon
                  v-else-if="option.icon === 'moon'"
                  :size="20"
                  :class="
                    themeStore.themeMode === option.value
                      ? 'text-primary-foreground'
                      : 'text-indigo-400'
                  "
                />
                <Monitor
                  v-else
                  :size="20"
                  :class="themeStore.themeMode === option.value ? 'text-primary-foreground' : ''"
                />
                <span class="text-xs">{{ option.label }}</span>
              </Button>
            </div>
          </div>

          <Separator />

          <!-- 主题配色 -->
          <div class="space-y-3">
            <Label class="text-base font-medium">{{ t('settings.themeColor') }}</Label>
            <div class="grid grid-cols-5 gap-3">
              <Card
                v-for="theme in themeColorNames"
                :key="theme.key"
                :class="[
                  'p-0 cursor-pointer transition-all duration-200 hover:ring-2 hover:ring-primary/50 hover:scale-105',
                  themeStore.themeColor === theme.key
                    ? 'ring-2 ring-primary shadow-md scale-105'
                    : '',
                ]"
                role="button"
                :aria-label="`Select ${theme.translatedName} color`"
                :aria-pressed="themeStore.themeColor === theme.key"
                tabindex="0"
                @click="themeStore.setThemeColor(theme.key as ThemeColor)"
                @keydown.enter="themeStore.setThemeColor(theme.key as ThemeColor)"
                @keydown.space.prevent="themeStore.setThemeColor(theme.key as ThemeColor)"
              >
                <CardContent class="p-3 flex flex-col items-center gap-2">
                  <div
                    class="w-8 h-8 rounded-md shadow-sm ring-1 ring-black/5"
                    :style="{
                      background: `linear-gradient(135deg, ${theme.primaryColor} 50%, ${theme.darkPrimaryColor} 50%)`,
                    }"
                  />
                  <span class="text-xs font-medium whitespace-nowrap">{{
                    theme.translatedName
                  }}</span>
                </CardContent>
              </Card>
            </div>
          </div>

          <Separator />

          <!-- 圆角大小 -->
          <div class="space-y-3">
            <div class="flex items-center justify-between">
              <Label class="text-base font-medium">{{ t('settings.borderRadius') }}</Label>
              <span class="text-sm text-muted-foreground font-medium tabular-nums">{{
                radiusLabel
              }}</span>
            </div>
            <div class="flex items-center gap-1 px-1">
              <Button
                v-for="preset in radiusPresets"
                :key="preset.value"
                variant="ghost"
                size="sm"
                class="flex-1 min-w-0 h-7 text-xs"
                :class="
                  Math.round(themeStore.borderRadius * 16) === preset.value
                    ? 'bg-primary text-primary-foreground'
                    : ''
                "
                :aria-label="`Set border radius to ${preset.label}`"
                @click="setRadiusPreset(preset.value)"
              >
                {{ preset.label }}
              </Button>
            </div>
            <Slider
              v-model="radiusValue"
              :min="0"
              :max="16"
              :step="1"
              class="flex-1"
              aria-label="Adjust border radius"
            />
          </div>

          <Separator />

          <!-- 布局模式 -->
          <div class="space-y-3">
            <Label class="text-base font-medium">{{ t('settings.layoutMode') }}</Label>
            <div class="grid grid-cols-3 gap-2">
              <Button
                v-for="option in layoutModeOptions"
                :key="option.value"
                :variant="themeStore.layoutMode === option.value ? 'default' : 'outline'"
                class="flex flex-col items-center gap-2 h-auto py-3 px-2 transition-all duration-200"
                :aria-label="`Switch to ${option.label}`"
                :aria-pressed="themeStore.layoutMode === option.value"
                @click="themeStore.setLayoutMode(option.value)"
              >
                <div
                  class="w-10 h-7 rounded border flex-shrink-0 overflow-hidden"
                  :class="
                    themeStore.layoutMode === option.value
                      ? 'border-primary-foreground/20 bg-primary-foreground/10'
                      : 'border-border bg-muted/50'
                  "
                >
                  <div
                    v-if="option.icon === 'sidebar'"
                    class="w-2.5 h-full"
                    :class="
                      themeStore.layoutMode === option.value
                        ? 'bg-primary-foreground/60'
                        : 'bg-primary/60'
                    "
                  />
                  <div
                    v-else-if="option.icon === 'topnav'"
                    class="h-2 w-full"
                    :class="
                      themeStore.layoutMode === option.value
                        ? 'bg-primary-foreground/60'
                        : 'bg-primary/60'
                    "
                  />
                  <template v-else-if="option.icon === 'mixed'">
                    <div
                      class="h-2 w-full"
                      :class="
                        themeStore.layoutMode === option.value
                          ? 'bg-primary-foreground/60'
                          : 'bg-primary/60'
                      "
                    />
                    <div
                      class="w-2.5 h-[calc(100%-8px)]"
                      :class="
                        themeStore.layoutMode === option.value
                          ? 'bg-primary-foreground/40'
                          : 'bg-primary/40'
                      "
                    />
                  </template>
                  <template v-else-if="option.icon === 'double'">
                    <div
                      class="w-2 h-full"
                      :class="
                        themeStore.layoutMode === option.value
                          ? 'bg-primary-foreground/40'
                          : 'bg-primary/40'
                      "
                    />
                    <div
                      class="w-2.5 h-full ml-px"
                      :class="
                        themeStore.layoutMode === option.value
                          ? 'bg-primary-foreground/60'
                          : 'bg-primary/60'
                      "
                    />
                  </template>
                  <template v-else-if="option.icon === 'mixed-double'">
                    <div
                      class="h-2 w-full"
                      :class="
                        themeStore.layoutMode === option.value
                          ? 'bg-primary-foreground/60'
                          : 'bg-primary/60'
                      "
                    />
                    <div
                      class="w-2 h-[calc(100%-8px)]"
                      :class="
                        themeStore.layoutMode === option.value
                          ? 'bg-primary-foreground/40'
                          : 'bg-primary/40'
                      "
                    />
                    <div
                      class="w-2.5 h-[calc(100%-8px)] ml-px"
                      :class="
                        themeStore.layoutMode === option.value
                          ? 'bg-primary-foreground/60'
                          : 'bg-primary/60'
                      "
                    />
                  </template>
                </div>
                <span class="text-xs font-medium">{{ option.label }}</span>
              </Button>
            </div>
          </div>

          <!-- 内容宽度设置 -->
          <Separator />

          <div class="space-y-3">
            <Label class="text-base font-medium">{{ t('settings.contentWidth') }}</Label>
            <div class="grid grid-cols-2 gap-3">
              <Button
                v-for="option in contentWidthOptions"
                :key="option.value"
                :variant="themeStore.layoutVariant === option.value ? 'default' : 'outline'"
                class="flex flex-col items-center gap-1 h-auto py-3 transition-all duration-200"
                :aria-label="`Select ${option.label}`"
                :aria-pressed="themeStore.layoutVariant === option.value"
                @click="themeStore.setLayoutVariant(option.value)"
              >
                <span class="font-medium">{{ option.label }}</span>
                <span
                  class="text-xs"
                  :class="
                    themeStore.layoutVariant === option.value
                      ? 'text-primary-foreground/80'
                      : 'text-muted-foreground'
                  "
                  >{{ option.description }}</span
                >
              </Button>
            </div>
          </div>

          <!-- 标签栏设置 -->
          <Separator />

          <div class="space-y-3">
            <Label class="text-base font-medium">{{ t('settings.tabsSettings') }}</Label>
            <div class="space-y-3">
              <div class="flex items-center justify-between py-2">
                <div class="flex flex-col gap-0.5">
                  <Label class="text-sm font-medium">{{ t('settings.showTabs') }}</Label>
                  <span class="text-xs text-muted-foreground">{{
                    t('settings.showTabsDesc')
                  }}</span>
                </div>
                <Switch
                  :model-value="themeStore.showTabs"
                  @update:model-value="themeStore.setShowTabs($event)"
                />
              </div>
              <div class="flex items-center justify-between py-2">
                <div class="flex flex-col gap-0.5">
                  <Label class="text-sm font-medium">{{ t('settings.tabsFixed') }}</Label>
                  <span class="text-xs text-muted-foreground">{{
                    t('settings.tabsFixedDesc')
                  }}</span>
                </div>
                <Switch
                  :model-value="themeStore.tabsFixed"
                  :disabled="!themeStore.showTabs"
                  @update:model-value="themeStore.setTabsFixed($event)"
                />
              </div>
            </div>
          </div>

          <!-- 侧边栏折叠开关 - 仅在支持折叠的布局模式下显示 -->
          <template v-if="showSidebarCollapse">
            <Separator />

            <div class="flex items-center justify-between py-2">
              <div class="flex flex-col gap-0.5">
                <Label class="text-base font-medium">{{ t('settings.sidebarCollapse') }}</Label>
                <span class="text-xs text-muted-foreground">{{
                  t('settings.sidebarCollapseDesc')
                }}</span>
              </div>
              <Switch
                :model-value="themeStore.sidebarCollapsed"
                @update:model-value="themeStore.setSidebarCollapsed($event)"
              />
            </div>
          </template>

          <!-- 侧边栏宽度设置 - 仅在 sidebar 或 mixed 模式下显示 -->
          <template v-if="showSidebarWidthSetting">
            <Separator />

            <div class="space-y-3">
              <div class="flex items-center justify-between">
                <Label class="text-base font-medium">{{ t('settings.sidebarWidth') }}</Label>
                <span class="text-sm text-muted-foreground font-medium tabular-nums">{{
                  sidebarWidthLabel
                }}</span>
              </div>
              <div class="flex items-center gap-4 px-1">
                <span class="text-xs text-muted-foreground">{{
                  t('settings.sidebarWidthNarrow')
                }}</span>
                <Slider
                  v-model="sidebarWidthValue"
                  :min="sidebarWidthRange.min"
                  :max="sidebarWidthRange.max"
                  :step="10"
                  class="flex-1"
                  aria-label="Adjust sidebar width"
                />
                <span class="text-xs text-muted-foreground">{{
                  t('settings.sidebarWidthWide')
                }}</span>
              </div>
              <div class="flex items-center justify-center gap-2 p-3 bg-muted/50 rounded-lg">
                <div
                  class="h-8 bg-primary/60 rounded transition-all duration-200"
                  :style="{
                    width: `${Math.min(100, (themeStore.sidebarWidth / sidebarWidthRange.max) * 100)}%`,
                  }"
                />
              </div>
            </div>
          </template>

          <Separator />

          <!-- 重置按钮 -->
          <div class="pt-2">
            <Button
              variant="outline"
              class="w-full"
              aria-label="Reset to default settings"
              @click="handleReset"
            >
              <RotateCcw :size="16" class="mr-2" />
              {{ t('settings.resetToDefault') }}
            </Button>
          </div>
        </div>
      </ScrollArea>
    </SheetContent>
  </Sheet>
</template>
