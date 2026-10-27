import { http } from './http'
import type {
  Cm3IndexChildrenResponse,
  Cm3IndexDto,
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
  IndexEntryInputDto,
  MedicalCodeDto,
  PagedResult,
  TumorIndexChildrenResponse,
  TumorIndexDto,
  TumorIndexEntryInputDto,
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

  getTermSynonymRows: (params?: { page?: number; pageSize?: number; keyword?: string }) =>
    http.get<PagedResult<{ id: string; term: string; normalizedTerm: string; entityType: string; codeSystemCode: string; code: string }>>(
      '/api/v1/knowledge/term-synonyms', params),

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

  getCm3Index: (params?: { page?: number; pageSize?: number; keyword?: string; leadWordStatus?: number; indexLevel?: number }) =>
    http.get<PagedResult<Cm3IndexDto>>('/api/v1/knowledge/cm3-index', params),

  getCm3IndexChildren: (params: { parentSeqNo?: number; leadWordStatus?: number; offset?: number; limit?: number }) =>
    http.get<Cm3IndexChildrenResponse>('/api/v1/knowledge/cm3-index/children', params),

  getTumorIndex: (params?: { page?: number; pageSize?: number; keyword?: string; indexLevel?: number }) =>
    http.get<PagedResult<TumorIndexDto>>('/api/v1/knowledge/tumor-index', params),

  getTumorIndexChildren: (params: { parentSeqNo?: number; offset?: number; limit?: number }) =>
    http.get<TumorIndexChildrenResponse>('/api/v1/knowledge/tumor-index/children', params),

  createIcdIndexEntry: (data: IndexEntryInputDto) =>
    http.post<{ id: string; seqNo: number }>('/api/v1/knowledge/icd-index', data),

  updateIcdIndexEntry: (id: string, data: IndexEntryInputDto) =>
    http.put<{ id: string; seqNo: number }>(`/api/v1/knowledge/icd-index/${id}`, data),

  deleteIcdIndexEntry: (id: string) =>
    http.delete<{ deleted: boolean }>(`/api/v1/knowledge/icd-index/${id}`),

  createCm3IndexEntry: (data: IndexEntryInputDto) =>
    http.post<{ id: string; seqNo: number }>('/api/v1/knowledge/cm3-index', data),

  updateCm3IndexEntry: (id: string, data: IndexEntryInputDto) =>
    http.put<{ id: string; seqNo: number }>(`/api/v1/knowledge/cm3-index/${id}`, data),

  deleteCm3IndexEntry: (id: string) =>
    http.delete<{ deleted: boolean }>(`/api/v1/knowledge/cm3-index/${id}`),

  createTumorIndexEntry: (data: TumorIndexEntryInputDto) =>
    http.post<{ id: string; seqNo: number }>('/api/v1/knowledge/tumor-index', data),

  updateTumorIndexEntry: (id: string, data: TumorIndexEntryInputDto) =>
    http.put<{ id: string; seqNo: number }>(`/api/v1/knowledge/tumor-index/${id}`, data),

  deleteTumorIndexEntry: (id: string) =>
    http.delete<{ deleted: boolean }>(`/api/v1/knowledge/tumor-index/${id}`),
}
