import type { SupportedLocale } from '@/i18n'
import { acceptHMRUpdate, defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { STORAGE_KEYS } from '@/constants/common'
import {
  getCurrentLocale,
  initI18n,
  setLocale as setI18nLocale,
  toggleLocale as toggleI18nLocale,
} from '@/i18n'
import { localeNames, preloadLocaleMessages, supportedLocales } from '@/i18n/locales'

/**
 * 语言状态管理 Store
 * 管理应用的语言设置和切换
 */
export const useLocaleStore = defineStore(
  'locale',
  () => {
    // 安全获取当前 locale (i18n 可能未初始化, 此时用 localStorage fallback)
    const currentLocale = ref<SupportedLocale>((() => {
      try {
        return getCurrentLocale()
      } catch {
        // i18n 还没初始化, 从 localStorage 读
        try {
          const stored = localStorage.getItem('tabtab-locale')
          if (stored) {
            const p = JSON.parse(stored)
            if (p?.currentLocale === 'zh-CN' || p?.currentLocale === 'en-US') return p.currentLocale
          }
        } catch { /* ignore */ }
        return 'zh-CN'
      }
    })())
    const isLoading = ref(false)
    const error = ref<string | null>(null)

    const currentLocaleName = computed(() => localeNames[currentLocale.value])

    const availableLocales = computed(() =>
      supportedLocales.map((locale) => ({
        value: locale,
        label: localeNames[locale],
      })),
    )

    const isZhCN = computed(() => currentLocale.value === 'zh-CN')
    const isEnUS = computed(() => currentLocale.value === 'en-US')

    /**
     * 执行异步操作并处理状态
     */
    async function executeAsync<T>(
      operation: () => Promise<T>,
      errorMessage: string,
    ): Promise<T | null> {
      isLoading.value = true
      error.value = null

      try {
        return await operation()
      } catch (err) {
        error.value = err instanceof Error ? err.message : errorMessage
        console.error(errorMessage, err)
        return null
      } finally {
        isLoading.value = false
      }
    }

    /**
     * 更新语言后的通用处理
     * 强制刷新页面确保所有 t() 重新计算（兜底策略）
     */
    function afterLocaleChange(locale: SupportedLocale): void {
      currentLocale.value = locale
      preloadLocaleMessages(locale === 'zh-CN' ? 'en-US' : 'zh-CN')
      // 触发一次空 setTimeout 刷新所有 computed
      setTimeout(() => {
        // 通知整个应用 locale 变化（通过 dispatchEvent）
        window.dispatchEvent(new CustomEvent('app:locale-changed', { detail: { locale } }))
      }, 0)
    }

    /**
     * 设置语言
     */
    async function changeLocale(locale: SupportedLocale): Promise<boolean> {
      console.log('[locale store] changeLocale called with:', locale)
      console.log('[locale store] currentLocale.value:', currentLocale.value)

      if (locale === currentLocale.value) return true

      const result = await executeAsync(async () => {
        console.log('[locale store] calling setI18nLocale...')
        const success = await setI18nLocale(locale)
        console.log('[locale store] setI18nLocale result:', success)
        if (success) afterLocaleChange(locale)
        return success
      }, '切换语言失败')

      console.log('[locale store] changeLocale result:', result)
      return result ?? false
    }

    /**
     * 切换语言
     */
    async function toggleLocale(): Promise<SupportedLocale | null> {
      return await executeAsync(async () => {
        const newLocale = await toggleI18nLocale()
        if (newLocale) afterLocaleChange(newLocale)
        return newLocale
      }, '切换语言失败')
    }

    /**
     * 初始化语言设置
     */
    async function init(): Promise<boolean> {
      const result = await executeAsync(async () => {
        const success = await initI18n()
        if (success) {
          currentLocale.value = getCurrentLocale()
          document.documentElement.setAttribute('lang', currentLocale.value)
          preloadLocaleMessages(currentLocale.value === 'zh-CN' ? 'en-US' : 'zh-CN')
        }
        return success
      }, '初始化语言设置失败')

      return result ?? false
    }

    function clearError() {
      error.value = null
    }

    return {
      currentLocale,
      isLoading,
      error,
      currentLocaleName,
      availableLocales,
      isZhCN,
      isEnUS,
      changeLocale,
      toggleLocale,
      init,
      clearError,
    }
  },
  {
    persist: {
      key: STORAGE_KEYS.LOCALE,
      pick: ['currentLocale'],
    },
  },
)

if (import.meta.hot) {
  import.meta.hot.accept(acceptHMRUpdate(useLocaleStore, import.meta.hot))
}
