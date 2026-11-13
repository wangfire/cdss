import { describe, expect, it } from 'vitest'

/**
 * 统一响应信封解析逻辑测试。
 *
 * 后端成功响应 = { code: 0, message: "ok", data: ... }
 * 后端错误响应 = { code: 非0, message: "...", traceId: "...", data: null }
 * 前端 http.ts 在 code !== 0 时抛 ApiError。
 *
 * 这里测试信封的形状约定，确保后端与前端对"成功"的定义一致。
 */
describe('ApiEnvelope 解析', () => {
  interface ApiEnvelope<T = unknown> {
    code: number
    message: string
    data: T | null
  }

  class ApiError extends Error {
    code: number
    status: number
    data: unknown

    constructor(message: string, code: number, status: number, data?: unknown) {
      super(message)
      this.name = 'ApiError'
      this.code = code
      this.status = status
      this.data = data
    }
  }

  function parseEnvelope<T>(text: string, httpStatus: number): T {
    let payload: ApiEnvelope<T> | null = null
    try {
      payload = text ? JSON.parse(text) : null
    } catch {
      // 非 JSON 响应
    }

    if (!payload) {
      throw new ApiError('响应解析失败', -1, httpStatus, text)
    }

    if (payload.code !== 0) {
      throw new ApiError(payload.message || '业务错误', payload.code, httpStatus, payload)
    }

    return payload.data as T
  }

  it('成功响应返回 data', () => {
    const json = JSON.stringify({ code: 0, message: 'ok', data: { id: 'x' } })
    expect(parseEnvelope(json, 200)).toEqual({ id: 'x' })
  })

  it('data 为 null 时成功响应返回 null', () => {
    const json = JSON.stringify({ code: 0, message: 'ok', data: null })
    expect(parseEnvelope(json, 200)).toBeNull()
  })

  it('业务错误抛 ApiError 且携带 code 和 message', () => {
    const json = JSON.stringify({
      code: 'validation_error',
      message: '用户名或密码错误。',
      traceId: 'abc',
      data: null,
    })
    try {
      parseEnvelope(json, 400)
      expect.unreachable('应该抛出 ApiError')
    } catch (e: any) {
      expect(e).toBeInstanceOf(ApiError)
      expect(e.code).toBe('validation_error')
      expect(e.message).toBe('用户名或密码错误。')
      expect(e.status).toBe(400)
    }
  })

  it('非 JSON 响应抛解析失败', () => {
    try {
      parseEnvelope('<html>502</html>', 502)
      expect.unreachable('应该抛出 ApiError')
    } catch (e: any) {
      expect(e).toBeInstanceOf(ApiError)
      expect(e.code).toBe(-1)
    }
  })

  it('空响应体抛解析失败', () => {
    try {
      parseEnvelope('', 200)
      expect.unreachable('应该抛出 ApiError')
    } catch (e: any) {
      expect(e).toBeInstanceOf(ApiError)
    }
  })

  it('message 为空串时回退到默认文案', () => {
    const json = JSON.stringify({ code: 500, message: '', data: null })
    try {
      parseEnvelope(json, 500)
      expect.unreachable('应该抛出 ApiError')
    } catch (e: any) {
      expect(e.message).toBe('业务错误')
    }
  })
})
