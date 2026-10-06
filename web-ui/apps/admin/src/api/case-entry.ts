import { http } from './http'
import type { CaseEntryRequest, CaseEntryResponse } from './types-cdss'

/** WP-B2-3：录入接口只受理（任务经 Outbox 异步执行），推荐由前端按 codingTaskId 轮询获取。 */
export const caseEntryApi = {
  createCase: (data: CaseEntryRequest) =>
    http.post<CaseEntryResponse>('/api/v1/coding/case-entry', data),
}
