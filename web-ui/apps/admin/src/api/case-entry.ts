import { http } from './http'
import type { CaseEntryRequest, CaseEntryResponse } from './types-cdss'

/** 流水线同步执行，单条病例可能耗时数十秒，超时放宽到 120s。 */
export const caseEntryApi = {
  createCase: (data: CaseEntryRequest) =>
    http.post<CaseEntryResponse>('/api/v1/coding/case-entry', data, { timeout: 120_000 }),
}
