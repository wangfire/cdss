import { http } from './http'
import type { HealthResponse, ReadinessResponse } from './types-cdss'

export const healthApi = {
  check: () => http.get<HealthResponse>('/api/v1/health', undefined, { auth: false }),
  readiness: () => http.get<ReadinessResponse>('/api/v1/health/ready', undefined, { auth: false }),
}
