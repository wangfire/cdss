﻿/**
 * Vue I18n 配置
 */
import type { SupportedLocale } from './locales'
import { ref, watch } from 'vue'
import { createI18n } from 'vue-i18n'
import { STORAGE_KEYS } from '@/constants/common'
import { getBrowserLocale, isSupportedLocale, loadLocaleMessages } from './locales'

function loadLocaleFromStorage(): SupportedLocale | null {
  try {
    const stored = localStorage.getItem(STORAGE_KEYS.LOCALE)
    const parsed = stored ? JSON.parse(stored) : null
    return parsed?.currentLocale && isSupportedLocale(parsed.currentLocale)
      ? parsed.currentLocale
      : null
  } catch {
    return null
  }
}

function getInitialLocale(): SupportedLocale {
  return loadLocaleFromStorage() ?? getBrowserLocale()
}

export function updateHtmlLang(locale: SupportedLocale): void {
  document.documentElement.setAttribute('lang', locale)
}

const _localeRef = ref<SupportedLocale>(getInitialLocale())
let _i18nInstance: any = null

async function preloadAllMessages(): Promise<Record<string, any>> {
  const result: Record<string, any> = {}
  const locales: SupportedLocale[] = ['zh-CN', 'en-US']
  for (const loc of locales) {
    try {
      result[loc] = await loadLocaleMessages(loc)
    } catch (e) {
      console.error('Failed to preload ' + loc + ':', e)
      result[loc] = {}
    }
  }
  return result
}

export async function createAppI18n() {
  const allMessages = await preloadAllMessages()
  const i18n = createI18n({
    legacy: false,
    globalInjection: true,
    locale: getInitialLocale(),
    fallbackLocale: 'en-US',
    messages: allMessages as any,
    missingWarn: false,
    fallbackWarn: false,
    warnHtmlMessage: false,
    silentTranslationWarn: true,
  })
  watch(
    _localeRef,
    (newLocale) => {
      try {
        i18n.global.locale.value = newLocale
      } catch (e) {
        console.warn('Failed to sync i18n locale:', e)
      }
      updateHtmlLang(newLocale)
    },
    { immediate: true },
  )
  _i18nInstance = i18n
  return i18n
}

export function getCurrentLocale(): SupportedLocale {
  return _localeRef.value
}

export async function setLocale(locale: SupportedLocale): Promise<boolean> {
  if (!isSupportedLocale(locale)) {
    console.warn('Unsupported locale: ' + locale)
    return false
  }
  const currentLocale = getCurrentLocale()
  if (locale === currentLocale) {
    return true
  }
  try {
    _localeRef.value = locale
    return true
  } catch (e) {
    console.error('setLocale error:', e)
    return false
  }
}

export async function toggleLocale(): Promise<SupportedLocale | null> {
  const current = getCurrentLocale()
  const newLocale: SupportedLocale = current === 'zh-CN' ? 'en-US' : 'zh-CN'
  const success = await setLocale(newLocale)
  return success ? newLocale : null
}

export async function initI18n(): Promise<boolean> {
  try {
    await createAppI18n()
    updateHtmlLang(_localeRef.value)
    return true
  } catch (e) {
    console.error('initI18n failed:', e)
    return false
  }
}

export const localeRef = _localeRef

export const i18n = new Proxy({} as any, {
  get(_target, prop) {
    if (!_i18nInstance) {
      if (prop === 'global') {
        return { locale: { value: 'zh-CN' as string }, t: (key) => key }
      }
      return () => {}
    }
    return _i18nInstance[prop]
  },
})

export type { SupportedLocale, TranslationKeys } from './types'
export type { TypedT } from './types'
