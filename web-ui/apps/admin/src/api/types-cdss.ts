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

/** 任务状态分组计数（仪表盘统计卡）。 */
export interface WorkbenchTaskStatsResponse {
  total: number
  byStatus: { status: string; count: number }[]
}

/** 工作台任务服务端分页响应（WP-B 前端生产化）。 */
export interface WorkbenchTasksPageResponse {
  total: number
  page: number
  pageSize: number
  items: WorkbenchTaskResponse[]
}

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
  isEnabled: boolean
}

/** ICD 索引库节点树子节点响应。 */
export interface IcdIndexChildrenResponse {
  parentSeqNo: number
  total: number
  items: (IcdIndexDto & { hasChildren: boolean })[]
}

/** CM3（手术操作）索引库条目，列口径与 ICD 索引库一致（全列保留；leadWordStatus 0=有效，1=无效）。 */
export interface Cm3IndexDto {
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
  isEnabled: boolean
}

/** CM3 索引库节点树子节点响应。 */
export interface Cm3IndexChildrenResponse {
  parentSeqNo: number
  total: number
  items: (Cm3IndexDto & { hasChildren: boolean })[]
}

/**
 * 肿瘤索引库条目（表 tumor_index，"肿瘤，肿瘤性"部位表）：一行并排五个动态码，
 * 无主导词状态；jumpKind 0=无跳转，1=见，2=另见。
 */
export interface TumorIndexDto {
  id: string
  seqNo: number
  indexName: string
  indexNameEn: string | null
  indexLevel: number
  parentSeqNo: number
  fullIndexName: string
  codeMalignantPrimary: string | null
  codeMalignantSecondary: string | null
  codeInSitu: string | null
  codeBenign: string | null
  codeUncertain: string | null
  jumpKind: number
  jumpTarget: string | null
}

/** 肿瘤索引库节点树子节点响应。 */
export interface TumorIndexChildrenResponse {
  parentSeqNo: number
  total: number
  items: (TumorIndexDto & { hasChildren: boolean })[]
}

/** 索引库单行增改入参（ICD/CM3 共用）；字段留空表示沿用库中原值，seqNo 只在新增时生效。 */
export interface IndexEntryInputDto {
  seqNo?: number
  rawName?: string
  rawNameEn?: string
  indexName?: string
  indexCode?: string
  indexLevel?: number
  parentSeqNo?: number
  fullIndexName?: string
  leadWordStatus?: number
}

/** 肿瘤索引单行增改入参；字段留空表示沿用库中原值，seqNo 只在新增时生效。 */
export interface TumorIndexEntryInputDto {
  seqNo?: number
  indexName?: string
  indexNameEn?: string
  indexLevel?: number
  parentSeqNo?: number
  fullIndexName?: string
  codeMalignantPrimary?: string
  codeMalignantSecondary?: string
  codeInSitu?: string
  codeBenign?: string
  codeUncertain?: string
  jumpKind?: number
  jumpTarget?: string
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

/* ============== 流水线运行时配置（工作台设置抽屉，user.manage 权限） ============== */

export interface PipelineConfigResponse {
  icdTitleDirect: boolean
  cm3TitleDirect: boolean
  icdTitleReverse: boolean
  vectorEnabled: boolean
  rerankEnabled: boolean
  ruleEngineEnabled: boolean
  updatedAt: string | null
}

export interface PipelineRerunResponse {
  rerunCount: number
}

/* ============== 编码录入：诊断/手术名称解析（主导词全量匹配 → 修饰词 → 有效编码） ============== */

export interface NameLookupRequest {
  kind: 'diagnosis' | 'procedure'
  text: string
  topK?: number | null
}

/** 字典标题直达命中（标题与输入逐字相等或被完整包含）。 */
export interface NameLookupDictionaryDirect {
  codeSystem: string
  code: string
  title: string
  depth: number
}

/** 全量匹配主导词：term 为索引原始行名（如 梗死，梗塞），displayTerm 非空时为同义族行的
 *  直观显示词面（梗死）；redirectKey 非空表示经失效主导词转示行锚定（阿米巴 → 阿米巴病）。 */
export interface NameLookupLeadTerm {
  seqNo: number
  term: string
  displayTerm: string | null
  hitAlias: string
  coveredChars: number
  position: number
  tailHit: boolean
  modifierPaths: NameLookupModifierPath[]
  redirectKey: string | null
}

/** 一个主导词下按输入走出的修饰词链路（path[0] 恒为主导词）。 */
export interface NameLookupModifierPath {
  path: string[]
  pathKey: string
  indexCode: string | null
  fromAncestorFallback: boolean
  jumps: string[]
  seeAlsoHint: boolean
  codes: NameLookupCodeItem[]
}

/** 链路展开出的候选码（来自字典展开或显式索引回退）。 */
export interface NameLookupCodeItem {
  codeSystem: string
  code: string
  title: string
  score: number
  isIndexFallback: boolean
  note: string | null
}

/** 合并后的最终推荐（跨主导词去重；dictionaryDirect=true 表示来自字典标题直达）。 */
export interface NameLookupRecommendation {
  codeSystem: string
  code: string
  title: string
  score: number
  isIndexFallback: boolean
  dictionaryDirect: boolean
  leadTerm: string | null
  path: string[]
  note: string | null
}

/** 标题通道开关（与流水线运行时配置 pipeline_runtime_config "v22lite" 同源，随解析响应回显）。 */
export interface NameLookupTitleChannelsInfo {
  icdTitleDirect: boolean
  cm3TitleDirect: boolean
  icdTitleReverse: boolean
}

export interface NameLookupResponse {
  kind: string
  requestText: string
  normalizedText: string
  codeSystem: string
  titleChannels: NameLookupTitleChannelsInfo
  dictionaryDirects: NameLookupDictionaryDirect[]
  leadTerms: NameLookupLeadTerm[]
  recommendations: NameLookupRecommendation[]
  degradedFlags: string[]
}

/** 最终编码提交（保存为病例后回填已确认编码用）。 */
export interface FinalResultItem {
  resultType: string
  codeSystem: string
  code: string
  title: string
  sourceRecommendationId?: string | null
}

export interface FinalResultSubmitRequest {
  codingTaskId?: string | null
  diagnosisInputId?: string | null
  items: FinalResultItem[]
}

export interface FinalResultSubmitResponse {
  codingTaskId: string
  finalCodingCount: number
  confirmedAt: string
}
