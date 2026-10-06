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

/** ICD 索引库条目（全列保留；leadWordStatus 0=有效，1=无效）。 */
export interface IcdIndexDto {
  id: string
  seqNo: number
  rawName: string
  rawNameEn: string | null
  indexName: string
  indexCode: string | null
  indexLevel: number
  parentSeqNo: number
  fullIndexName: string
  leadWordStatus: number
}

/** ICD 索引库节点树子节点响应。 */
export interface IcdIndexChildrenResponse {
  parentSeqNo: number
  total: number
  items: (IcdIndexDto & { hasChildren: boolean })[]
}

export interface IcdTermSynonymVariantDto {
  id: string
  variantTerm: string
  isEnabled: boolean
}

/**
 * ICD 索引链路同义词组（表 icd_term_synonym，诊断/手术通道分开，推荐链路实际读取的就是这张表）。
 * 一行 (规范词, 等同词) 是一条等同边，快照按连通分量合成等价类，类内任意两词匹配时可互换。
 */
export interface IcdTermSynonymGroupDto {
  termType: string
  canonicalTerm: string
  variants: IcdTermSynonymVariantDto[]
}

/** 单条同义词增改入参；字段留空表示沿用库中原值。 */
export interface IcdTermSynonymInputDto {
  termType?: string
  canonicalTerm?: string
  variantTerm?: string
  isEnabled?: boolean
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

export interface DependencyStatusInfo {
  status: 'healthy' | 'unhealthy' | 'disabled'
  error: string | null
}

/** /api/v1/health/ready 裸 JSON（不走 ApiEnvelope），HTTP 200/503 均携带该结构 */
export interface ReadinessResponse {
  status: 'healthy' | 'degraded' | 'unhealthy'
  dependencies: Record<string, DependencyStatusInfo>
}
