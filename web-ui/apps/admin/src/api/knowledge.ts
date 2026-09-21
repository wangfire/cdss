import { http } from './http'
import type {
  CodeSystemDto,
  CodingRuleDto,
  ImportCodeSystemRequest,
  ImportCodeSystemResponse,
  ImportCodingRulesRequest,
  ImportCodingRulesResponse,
  MedicalCodeDto,
  PagedResult,
  TermSynonymDto,
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

  getTermSynonyms: (params?: { page?: number; pageSize?: number; keyword?: string }) =>
    http.get<PagedResult<TermSynonymDto>>('/api/v1/knowledge/term-synonyms', params),

  getCodingRules: (params?: { page?: number; pageSize?: number; codeSystem?: string }) =>
    http.get<PagedResult<CodingRuleDto>>('/api/v1/knowledge/coding-rules', params),
}
