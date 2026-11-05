import { http } from './http'
import type {
  CaseEntryRequest,
  CaseEntryResponse,
  FinalResultSubmitRequest,
  FinalResultSubmitResponse,
  NameLookupRequest,
  NameLookupResponse,
} from './types-cdss'

/**
 * 编码录入：诊断/手术名称 → 主导词全量匹配拆分 → 修饰词下钻 → 有效编码推荐。
 * 解析无状态不落库；确认结果经"保存为病例 + 最终编码提交"走既有落库端点。
 */
export const codingEntryApi = {
  /** 名称即时解析（kind=diagnosis 走 ICD-10 索引，kind=procedure 走 CM3 索引）。 */
  lookupName: (data: NameLookupRequest) =>
    http.post<NameLookupResponse>('/api/v1/coding/name-lookup', data),

  /** 保存为病例：整病例落库并触发推荐流水线，返回 codingTaskId。 */
  createCase: (data: CaseEntryRequest) =>
    http.post<CaseEntryResponse>('/api/v1/coding/case-entry', data),

  /** 已确认编码提交为最终编码（绑定保存病例产生的 codingTaskId）。 */
  submitFinalResults: (data: FinalResultSubmitRequest) =>
    http.post<FinalResultSubmitResponse>('/api/v1/final-results/submit', data),
}
