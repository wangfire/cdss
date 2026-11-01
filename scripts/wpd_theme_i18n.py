# -*- coding: utf-8 -*-
"""ThemeSwitcher.vue：主题标签 i18n（浅色/深色/系统 + aria-label + sr-only）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"web-ui\apps\admin\src\components\ThemeSwitcher.vue"
c = open(p, encoding="utf-8").read()

# 1. script 加 useI18n
old_import = """import { useThemeStore, type ThemeMode } from '@/stores/theme'
import { Sun, Moon, Monitor } from 'lucide-vue-next'"""
new_import = """import { useThemeStore, type ThemeMode } from '@/stores/theme'
import { useI18n } from 'vue-i18n'
import { Sun, Moon, Monitor } from 'lucide-vue-next'"""
assert old_import in c, "import anchor"
c = c.replace(old_import, new_import, 1)

old_store = "const themeStore = useThemeStore()"
new_store = """const themeStore = useThemeStore()
const { t } = useI18n()"""
assert old_store in c, "store anchor"
c = c.replace(old_store, new_store, 1)

# 2. themeOptions label 改 i18n key
old_opts = """const themeOptions: { value: ThemeMode; label: string; icon: string }[] = [
  { value: 'light', label: '浅色', icon: 'sun' },
  { value: 'dark', label: '深色', icon: 'moon' },
  { value: 'system', label: '系统', icon: 'monitor' },
]"""
new_opts = """const themeOptions: { value: ThemeMode; labelKey: string; icon: string }[] = [
  { value: 'light', labelKey: 'theme.light', icon: 'sun' },
  { value: 'dark', labelKey: 'theme.dark', icon: 'moon' },
  { value: 'system', labelKey: 'theme.system', icon: 'monitor' },
]"""
assert old_opts in c, "opts anchor"
c = c.replace(old_opts, new_opts, 1)

# 3. getThemeLabel 用 t()
old_label = """function getThemeLabel(mode: ThemeMode): string {
  const option = themeOptions.find((o) => o.value === mode)
  return option?.label || '系统'
}"""
new_label = """function getThemeLabel(mode: ThemeMode): string {
  const option = themeOptions.find((o) => o.value === mode)
  return option ? t(option.labelKey) : t('theme.system')
}"""
assert old_label in c, "label fn anchor"
c = c.replace(old_label, new_label, 1)

# 4. aria-label 与 sr-only
c = c.replace(
    ':aria-label="`切换主题，当前: ${getThemeLabel(themeStore.themeMode)}`"',
    ':aria-label="t(\'theme.switchAriaLabel\', { mode: getThemeLabel(themeStore.themeMode) })"',
)
c = c.replace('<span class="sr-only">切换主题</span>', '<span class="sr-only">{{ t(\'theme.switchAriaLabel\', { mode: getThemeLabel(themeStore.themeMode) }) }}</span>')
c = c.replace('<p>{{ getThemeLabel(themeStore.themeMode) }}模式</p>', '<p>{{ getThemeLabel(themeStore.themeMode) }}{{ t(\'theme.modeSuffix\') }}</p>')

open(p, "w", encoding="utf-8").write(c)
print("ThemeSwitcher i18n done")
