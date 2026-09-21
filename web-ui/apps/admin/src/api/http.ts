/**
 * 后端统一响应包装
 */
export interface ApiEnvelope<T = unknown> {
  code: number
  message: string
  data: T | null
}

export class ApiError extends Error {
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

import { API_CONFIG, STORAGE_KEYS } from '@/constants/common'

/**
 * HTTP 客户端：基于浏览器原生 fetch。
 * 自动注入 JWT、解析统一响应、抛出 ApiError。
 */
class HttpClient {
  private baseUrl: string
  private timeout: number
  private onUnauthorized?: () => void

  constructor(baseUrl: string, timeout: number) {
    this.baseUrl = baseUrl.replace(/\/+$/, '')
    this.timeout = timeout
  }

  setUnauthorizedHandler(handler: () => void) {
    this.onUnauthorized = handler
  }

  private getToken(): string | null {
    return localStorage.getItem(STORAGE_KEYS.TOKEN)
  }

  private buildHeaders(extraHeaders?: HeadersInit, withAuth = true): Headers {
    const headers = new Headers(extraHeaders)
    headers.set('Accept', 'application/json')
    if (withAuth) {
      const token = this.getToken()
      if (token) headers.set('Authorization', `Bearer ${token}`)
      // 医院隔离上下文：从本地存储取登录时的 hospitalId，随每个请求注入。
      const hospitalId = localStorage.getItem(STORAGE_KEYS.HOSPITAL)
      if (hospitalId) headers.set('X-Hospital-Id', hospitalId)
    }
    return headers
  }

  private buildQueryString(query: Record<string, unknown>): string {
    const parts: string[] = []
    for (const key of Object.keys(query)) {
      const value = query[key]
      if (value === undefined || value === null || value === '') continue
      parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`)
    }
    return parts.join('&')
  }

  private async request<T>(
    method: string,
    path: string,
    body?: unknown,
    options: { auth?: boolean; headers?: Record<string, string>; query?: Record<string, unknown>; timeout?: number } = {},
  ): Promise<T> {
    const { auth = true, headers: extraHeaders, query, timeout } = options
    let url = this.baseUrl + (path.startsWith('/') ? path : `/${path}`)
    if (query) {
      const qs = this.buildQueryString(query)
      if (qs) url += (url.includes('?') ? '&' : '?') + qs
    }

    const init: RequestInit = {
      method,
      headers: this.buildHeaders(extraHeaders, auth),
    }

    if (body !== undefined && body !== null) {
      if (body instanceof FormData) {
        init.body = body
      } else {
        const headers = init.headers as Headers
        if (!headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
        init.body = typeof body === 'string' ? body : JSON.stringify(body)
      }
    }

    const controller = new AbortController()
    init.signal = controller.signal
    const timer = setTimeout(() => controller.abort(), timeout ?? this.timeout)

    let response: Response
    try {
      response = await fetch(url, init)
    } catch (e: any) {
      clearTimeout(timer)
      if (e?.name === 'AbortError') {
        throw new ApiError('请求超时', -1, 0)
      }
      throw new ApiError(`网络错误: ${e?.message || e}`, -1, 0)
    }
    clearTimeout(timer)

    const text = await response.text()
    let payload: ApiEnvelope<T> | null = null
    try {
      payload = text ? JSON.parse(text) : null
    } catch {
      // 非 JSON 响应
    }

    if (!response.ok) {
      if (response.status === 401) {
        this.onUnauthorized?.()
      }
      const msg = payload?.message || `HTTP ${response.status}`
      throw new ApiError(msg, payload?.code ?? response.status, response.status, payload)
    }

    if (!payload) {
      throw new ApiError('响应解析失败', -1, response.status, text)
    }

    if (payload.code !== 0) {
      throw new ApiError(payload.message || '业务错误', payload.code, response.status, payload)
    }

    return payload.data as T
  }

  get<T>(path: string, query?: Record<string, unknown>, options?: { auth?: boolean; timeout?: number }): Promise<T> {
    return this.request<T>('GET', path, undefined, { ...options, query })
  }
  post<T>(path: string, body?: unknown, options?: { auth?: boolean; timeout?: number }): Promise<T> {
    return this.request<T>('POST', path, body, options)
  }
  put<T>(path: string, body?: unknown, options?: { auth?: boolean; timeout?: number }): Promise<T> {
    return this.request<T>('PUT', path, body, options)
  }
  delete<T>(path: string, options?: { auth?: boolean }): Promise<T> {
    return this.request<T>('DELETE', path, undefined, options)
  }
}

export const http = new HttpClient(API_CONFIG.BASE_URL, API_CONFIG.TIMEOUT)
