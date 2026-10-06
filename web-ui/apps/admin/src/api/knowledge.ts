import { http } from './http'
import type {
  CodeSystemDto,
  CodingRuleDto,
  ImportCodeSystemRequest,
  ImportCodeSystemResponse,
  ImportCodingRulesRequest,
  ImportCodingRulesResponse,
  IcdIndexChildrenResponse,
  IcdIndexDto,
  IcdTermSynonymGroupDto,
  IcdTermSynonymInputDto,
  MedicalCodeDto,
  PagedResult,
} from './types-cdss'

export const knowledgeApi = {
  importCodeSystem: (data: ImportCodeSystemRequest) =>
    http.post<ImportCodeSystemResponse>('/api/v1/code-systems/import', data),

  importCodingRules: (data: ImportCodingRulesRequest) =>
    http.post<ImportCodingRulesResponse>('/api/v1/coding-rules/import', data),

  getCodeSystems: () =>
    http.get<CodeSystemDto[]>('/api/v1/knowledge/code-systems'),

  getMedicalCodes: (params?: { page?: number; pageSize?: number; codeSystem?: string; keyword?: string }) =>
    http.get<PagedResult<MedicalCodeDto>>('/api/v1/knowledge/medical-codes', params),

  getTermSynonyms: (params?: { termType?: string; keyword?: string }) =>
    http.get<{ total: number; items: IcdTermSynonymGroupDto[] }>(
      '/api/v1/knowledge/icd-term-synonym', params),

  createTermSynonym: (data: IcdTermSynonymInputDto) =>
    http.post<{ id: string }>('/api/v1/knowledge/icd-term-synonym', data),

  updateTermSynonym: (id: string, data: IcdTermSynonymInputDto) =>
    http.put<{ id: string }>(`/api/v1/knowledge/icd-term-synonym/${id}`, data),

  deleteTermSynonym: (id: string) =>
    http.delete<{ deleted: boolean }>(`/api/v1/knowledge/icd-term-synonym/${id}`),

  getCodingRules: (params?: { page?: number; pageSize?: number; codeSystem?: string }) =>
    http.get<PagedResult<CodingRuleDto>>('/api/v1/knowledge/coding-rules', params),

  getIcdIndex: (params?: { page?: number; pageSize?: number; keyword?: string; leadWordStatus?: number; indexLevel?: number }) =>
    http.get<PagedResult<IcdIndexDto>>('/api/v1/knowledge/icd-index', params),

  getIcdIndexChildren: (params: { parentSeqNo?: number; leadWordStatus?: number; offset?: number; limit?: number }) =>
    http.get<IcdIndexChildrenResponse>('/api/v1/knowledge/icd-index/children', params),
}
