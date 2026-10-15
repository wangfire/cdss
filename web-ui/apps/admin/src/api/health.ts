import { http } from './http'
import { API_CONFIG } from '@/constants/common'
import type { HealthResponse, ReadinessResponse } from './types-cdss'

/**
 * /api/v1/health/ready 是基础设施探针：裸 JSON（无 ApiEnvelope），
 * 且依赖不可用时返回 503 但 body 仍是完整就绪结构，因此不走通用 http 客户端。
 */
async function fetchReadiness(): Promise<ReadinessResponse> {
  const response = await fetch(`${API_CONFIG.BASE_URL}/api/v1/health/ready`, {
    headers: { Accept: 'application/json' },
  })
  const payload = await response.json().catch(() => null) as ReadinessResponse | null
  if (payload?.status) return payload
  throw new Error(`HTTP ${response.status}`)
}

export const healthApi = {
  check: () => http.get<HealthResponse>('/api/v1/health', undefined, { auth: false }),
  readiness: fetchReadiness,
}
