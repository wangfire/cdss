/**
 * CDSS 业务 API 类型定义（Phase 1 + Phase 2）
 */

/* ============== 编码任务 ============== */

export type CodingTaskStatus =
  | 'PENDING'
  | 'RUNNING'
  | 'SUCCESS'
  | 'FAILED'
  | 'RETRYING'
  | 'TIMEOUT'
  | 'CANCELLED'
  | 'HUMAN_REQUIRED'
  | 'PENDING_REVIEW'
  | 'ACCEPTED'
  | 'MODIFIED'
  | 'REJECTED'

export interface CreateCodingTaskRequest {
  visitId: string
  pipelineVersion: string
}

export interface CodingTaskResponse {
  id: string
  hospitalId: string
  visitId: string
  pipelineVersion: string
  status: CodingTaskStatus
  traceId: string
  createdAt: string
  completedAt: string | null
}

/* ============== 工作台 ============== */

export interface WorkbenchTaskResponse {
  taskId: string
  visitId: string
  status: CodingTaskStatus
  recommendationCount: number
  createdAt: string
  patientName: string | null
  medicalRecordNo: string | null
  admissionCount: number
  dischargeAt: string | null
}

/* ============== 编码推荐 ============== */

export interface RecommendationEvidenceResponse {
  sourceType: string
  sourceText: string
  matchText: string
  score: number
}

export interface CodingRecommendationResponse {
  id: string
  recommendationType: string
  codeSystem: string
  code: string
  title: string
  rank: number
  confidenceScore: number
  reviewStatus: string
  evidences: RecommendationEvidenceResponse[]
  diagnosisInputId?: string | null
  doctorDiagnosisText?: string | null
  isPrincipal?: boolean | null
  diagnosisOrder?: number | null
  diagnosisSourceType?: string | null
}

export interface CodingTaskRecommendationsResponse {
  taskId: string
  status: CodingTaskStatus
  recommendations: CodingRecommendationResponse[]
}

/* ============== 人工审核 ============== */

export interface ReviewedCodeItem {
  recommendationId: string | null
  resultType: string
  codeSystem: string
  code: string
  title: string
}

export interface ReviewCodingTaskRequest {
  reviewStatus: string
  finalCodes: ReviewedCodeItem[]
  comment?: string | null
}

export interface CodingReviewResponse {
  taskId: string
  reviewStatus: string
  finalCodeCount: number
}

/* ============== 知识库 ============== */

export interface MedicalCodeImportItem {
  code: string
  title: string
  codeType: string
  searchText: string | null
  isEnabled: boolean
}

export interface ImportCodeSystemRequest {
  codeSystem: string
  version: string
  codes: MedicalCodeImportItem[]
}

export interface ImportCodeSystemResponse {
  codeSystem: string
  importedCount: number
  updatedCount: number
}

export interface TermSynonymImportItem {
  term: string
  normalizedTerm: string
  entityType: string
  codeSystemCode?: string
  code?: string
}

export interface CodingRuleImportItem {
  ruleCode: string
  codeSystem: string
  codePattern: string
  ruleType: string
  severity: string
  message: string
  isEnabled: boolean
}

export interface ImportCodingRulesRequest {
  synonyms: TermSynonymImportItem[]
  rules: CodingRuleImportItem[]
}

export interface ImportCodingRulesResponse {
  synonymCount: number
  ruleCount: number
}

/* ============== 知识库查询 ============== */

export interface CodeSystemDto {
  id: string
  code: string
  name: string
  version: string
  createdAt: string
}

export interface MedicalCodeDto {
  id: string
  code: string
  title: string
  codeSystemCode: string
  searchText: string | null
  isEnabled: boolean
}

export interface TermSynonymDto {
  id: string
  term: string
  normalizedTerm: string
  entityType: string
  codeSystemCode: string | null
  code: string | null
}

export interface CodingRuleDto {
  id: string
  ruleCode: string
  codeSystem: string
  codePattern: string
  ruleType: string
  severity: string
  message: string
  isEnabled: boolean
}

export interface PagedResult<T> {
  total: number
  page: number
  pageSize: number
  items: T[]
}

/* ============== 追踪 ============== */

export interface TraceStepResponse {
  stepName: string
  status: string
  startedAt: string
  completedAt: string | null
  errorCode: string | null
}

export interface TraceResponse {
  traceId: string
  hospitalId: string
  taskId: string | null
  status: string
  startedAt: string
  completedAt: string | null
  steps: TraceStepResponse[]
}

export interface PipelineRunStepResponse {
  stage: string
  status: string
  errorCode: string | null
  startedAt: string
  completedAt: string | null
  durationMs: number | null
}

export interface PipelineRunIssueResponse {
  issueType: string
  riskLevel: string
  description: string
  currentCode: string | null
  diagnosisInputId: string | null
}

export interface PipelineRunResponse {
  runId: string | null
  traceId: string
  pipelineVersion: string | null
  status: string
  startedAt: string
  completedAt: string | null
  durationMs: number | null
  degradedFlags: string[]
  recommendationCount: number
  steps: PipelineRunStepResponse[]
  qualityIssues: PipelineRunIssueResponse[]
}

/* ============== 病例录入 ============== */

export interface CaseEntryRequest {
  patientName: string
  medicalRecordNo: string
  admissionCount?: number | null
  admissionAt: string
  dischargeAt?: string | null
  admissionDiagnoses?: string[]
  dischargeDiagnoses: string[]
  procedures?: string[]
  additionalDocumentContent?: string | null
}

export interface CaseEntryDiagnosisInput {
  id: string
  codingTaskId: string
  visitId: string
  sourceType: string
  originalText: string
  normalizedText: string | null
  isPrincipal: boolean
  diagnosisOrder: number
  status: string
}

export interface CaseEntryResponse {
  patientId: string
  visitId: string
  documentId: string
  codingTaskId: string
  pipelineVersion: string
  admissionCount: number
  codingStage: string
  diagnosisInputs: CaseEntryDiagnosisInput[]
  recommendationCount: number
}

/* ============== 健康检查 ============== */

export interface HealthResponse {
  status: string
  traceId: string
}

export interface ReadinessResponse {
  status: string
  checks: Record<string, { status: string; latencyMs?: number }>
}
