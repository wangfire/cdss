import { describe, expect, it } from 'vitest'

/**
 * i18n key 对齐测试：zh-CN 与 en-US 的叶子 key 必须完全一致。
 *
 * 如果一侧有 key 另一侧没有，切换语言后用户会看到原始 key 或英文回退。
 * 此前 DictionaryPickerDialog 用 t('common.close') 但两侧都没有该 key，
 * 且 missingWarn:false 静默吞掉了告警 —— 这类问题靠人工排查不可靠。
 */
describe('i18n key 对齐', () => {
  it('common 模块 zh/en 顶层 key 一致', async () => {
    const zh = await import('../i18n/locales/zh-CN/common')
    const en = await import('../i18n/locales/en-US/common')
    const zhKeys = new Set(Object.keys(zh.default))
    const enKeys = new Set(Object.keys(en.default))
    // 过滤非翻译 key（如 default 导出）
    zhKeys.delete('default')
    enKeys.delete('default')

    const onlyZh = [...zhKeys].filter(k => !enKeys.has(k))
    const onlyEn = [...enKeys].filter(k => !zhKeys.has(k))
    expect(onlyZh, `zh-CN 独有 key: ${onlyZh.join(', ')}`).toEqual([])
    expect(onlyEn, `en-US 独有 key: ${onlyEn.join(', ')}`).toEqual([])
  })

  it('common.close 必须存在（DictionaryPickerDialog 引用）', async () => {
    const zh = await import('../i18n/locales/zh-CN/common')
    const en = await import('../i18n/locales/en-US/common')
    expect(zh.default).toHaveProperty('close')
    expect(en.default).toHaveProperty('close')
    expect(typeof zh.default.close).toBe('string')
    expect(typeof en.default.close).toBe('string')
  })

  it('common HTTP 错误消息 key 必须存在（http.ts 引用）', async () => {
    const zh = await import('../i18n/locales/zh-CN/common')
    const en = await import('../i18n/locales/en-US/common')
    for (const key of ['httpTimeout', 'httpNetworkError', 'httpParseFailed', 'httpBusinessError']) {
      expect(zh.default).toHaveProperty(key)
      expect(en.default).toHaveProperty(key)
    }
  })
})
