using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.CodingRecommendations;

/// <summary>
/// 就诊数据就绪状态响应。Data Quality Gate 通过后 READY，否则 HUMAN_REQUIRED。
/// </summary>
public sealed record VisitReadinessResponse(
    [property: JsonPropertyName("visitId")] Guid VisitId,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("documentCount")] int DocumentCount,
    [property: JsonPropertyName("readyDocumentCount")] int ReadyDocumentCount,
    [property: JsonPropertyName("diagnosisInputCount")] int DiagnosisInputCount,
    [property: JsonPropertyName("codingTaskId")] Guid? CodingTaskId,
    [property: JsonPropertyName("degradedFlags")] IReadOnlyList<string> DegradedFlags,
    [property: JsonPropertyName("qualityIssues")] IReadOnlyList<QualityIssueResponse> QualityIssues);

/// <summary>
/// 结构化质量问题。
/// </summary>
public sealed record QualityIssueResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("issueType")] string IssueType,
    [property: JsonPropertyName("riskLevel")] string RiskLevel,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("factId")] Guid? FactId,
    [property: JsonPropertyName("evidenceIds")] IReadOnlyList<Guid> EvidenceIds,
    [property: JsonPropertyName("currentCode")] string? CurrentCode,
    [property: JsonPropertyName("suggestedCode")] string? SuggestedCode,
    [property: JsonPropertyName("status")] string Status);

/// <summary>
/// 临床事实响应。否定、确定性、时间性必须随事实一起返回，禁止只回原文。
/// </summary>
public sealed record ClinicalFactResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("factType")] string FactType,
    [property: JsonPropertyName("factName")] string FactName,
    [property: JsonPropertyName("normalizedValue")] string? NormalizedValue,
    [property: JsonPropertyName("originalValue")] string OriginalValue,
    [property: JsonPropertyName("negation")] bool Negation,
    [property: JsonPropertyName("certainty")] string Certainty,
    [property: JsonPropertyName("temporality")] string Temporality,
    [property: JsonPropertyName("confidence")] decimal Confidence,
    [property: JsonPropertyName("sourceDocumentId")] Guid? SourceDocumentId,
    [property: JsonPropertyName("sourceSectionId")] Guid? SourceSectionId,
    [property: JsonPropertyName("sourceStart")] int? SourceStart,
    [property: JsonPropertyName("sourceEnd")] int? SourceEnd,
    [property: JsonPropertyName("extractorVersion")] string ExtractorVersion);

/// <summary>
/// 推荐明细响应：候选、七维分数、证据、规则命中、质量问题和 Trace 引用。
/// </summary>
public sealed record RecommendationDetailResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("codingTaskId")] Guid CodingTaskId,
    [property: JsonPropertyName("diagnosisInputId")] Guid? DiagnosisInputId,
    [property: JsonPropertyName("recommendationType")] string RecommendationType,
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("finalScore")] decimal FinalScore,
    [property: JsonPropertyName("confidenceScore")] decimal ConfidenceScore,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("lifecycleStatus")] string LifecycleStatus,
    [property: JsonPropertyName("evidenceSufficiency")] decimal? EvidenceSufficiency,
    [property: JsonPropertyName("riskLevel")] string? RiskLevel,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("pipelineVersion")] string PipelineVersion,
    [property: JsonPropertyName("pipelineRunId")] Guid? PipelineRunId,
    [property: JsonPropertyName("recommendationVersion")] string? RecommendationVersion,
    [property: JsonPropertyName("knowledgeVersion")] string? KnowledgeVersion,
    [property: JsonPropertyName("ruleVersion")] string? RuleVersion,
    [property: JsonPropertyName("codingVersion")] string? CodingVersion,
    [property: JsonPropertyName("modelVersion")] string? ModelVersion,
    [property: JsonPropertyName("degradedFlags")] IReadOnlyList<string> DegradedFlags,
    [property: JsonPropertyName("isReadOnly")] bool IsReadOnly,
    [property: JsonPropertyName("scores")] RecommendationScoreResponse? Scores,
    [property: JsonPropertyName("candidates")] IReadOnlyList<CodingCandidateResponse> Candidates,
    [property: JsonPropertyName("evidences")] IReadOnlyList<RecommendationEvidenceResponse> Evidences,
    [property: JsonPropertyName("qualityIssues")] IReadOnlyList<QualityIssueResponse> QualityIssues);

/// <summary>
/// 七维分数响应。缺失维度不出现在列表中，由前端按 scoreProfile 归一化展示。
/// </summary>
public sealed record RecommendationScoreResponse(
    [property: JsonPropertyName("exactScore")] decimal? ExactScore,
    [property: JsonPropertyName("semanticScore")] decimal? SemanticScore,
    [property: JsonPropertyName("retrievalScore")] decimal? RetrievalScore,
    [property: JsonPropertyName("rerankScore")] decimal? RerankScore,
    [property: JsonPropertyName("ruleScore")] decimal? RuleScore,
    [property: JsonPropertyName("evidenceScore")] decimal? EvidenceScore,
    [property: JsonPropertyName("llmScore")] decimal? LlmScore,
    [property: JsonPropertyName("marginScore")] decimal? MarginScore,
    [property: JsonPropertyName("scoreProfile")] string ScoreProfile);

/// <summary>
/// 候选项响应：保留召回与排序过程分，支持可解释回放。
/// </summary>
public sealed record CodingCandidateResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("recallSource")] string RecallSource,
    [property: JsonPropertyName("exactScore")] decimal? ExactScore,
    [property: JsonPropertyName("bm25Score")] decimal? Bm25Score,
    [property: JsonPropertyName("vectorScore")] decimal? VectorScore,
    [property: JsonPropertyName("rerankScore")] decimal? RerankScore,
    [property: JsonPropertyName("ruleScore")] decimal? RuleScore,
    [property: JsonPropertyName("evidenceScore")] decimal? EvidenceScore,
    [property: JsonPropertyName("finalScore")] decimal FinalScore,
    [property: JsonPropertyName("rank")] int Rank);

/// <summary>
/// 推荐列表响应。默认只返回当前 PipelineVersion 的结果。
/// </summary>
public sealed record RecommendationListResponse(
    [property: JsonPropertyName("diagnosisInputId")] Guid? DiagnosisInputId,
    [property: JsonPropertyName("pipelineVersion")] string PipelineVersion,
    [property: JsonPropertyName("recommendations")] IReadOnlyList<RecommendationDetailResponse> Recommendations);

/// <summary>
/// 诊断输入批量提交请求。医生 / 编码员的原始诊断，AI 标准化不得覆盖。
/// </summary>
public sealed record DiagnosisInputBatchRequest(
    [property: JsonPropertyName("sourceType")] string SourceType,
    [property: JsonPropertyName("items")] IReadOnlyList<DiagnosisInputItem> Items);

/// <summary>
/// 单条诊断输入。
/// </summary>
public sealed record DiagnosisInputItem(
    [property: JsonPropertyName("originalText")] string OriginalText,
    [property: JsonPropertyName("isPrincipal")] bool IsPrincipal,
    [property: JsonPropertyName("diagnosisOrder")] int DiagnosisOrder,
    [property: JsonPropertyName("sourceDocumentId")] Guid? SourceDocumentId,
    [property: JsonPropertyName("sourceSectionId")] Guid? SourceSectionId);

/// <summary>
/// 诊断输入响应。
/// </summary>
public sealed record DiagnosisInputResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("codingTaskId")] Guid CodingTaskId,
    [property: JsonPropertyName("visitId")] Guid VisitId,
    [property: JsonPropertyName("sourceType")] string SourceType,
    [property: JsonPropertyName("originalText")] string OriginalText,
    [property: JsonPropertyName("normalizedText")] string? NormalizedText,
    [property: JsonPropertyName("isPrincipal")] bool IsPrincipal,
    [property: JsonPropertyName("diagnosisOrder")] int DiagnosisOrder,
    [property: JsonPropertyName("status")] string Status);

/// <summary>
/// 推荐触发请求。缺省复用任务已生成的诊断输入。
/// </summary>
public sealed record RecommendRequest(
    [property: JsonPropertyName("diagnosisInputIds")] IReadOnlyList<Guid>? DiagnosisInputIds);

/// <summary>
/// 病例录入请求：手工录入单条病案的基本信息与诊断 / 手术条目，
/// 落库后同步触发 V2.2-Lite 推荐流水线。
/// </summary>
public sealed record CaseEntryRequest(
    [property: JsonPropertyName("patientName")] string PatientName,
    [property: JsonPropertyName("medicalRecordNo")] string MedicalRecordNo,
    [property: JsonPropertyName("admissionCount")] int? AdmissionCount,
    [property: JsonPropertyName("admissionAt")] DateTimeOffset AdmissionAt,
    [property: JsonPropertyName("dischargeAt")] DateTimeOffset? DischargeAt,
    [property: JsonPropertyName("admissionDiagnoses")] IReadOnlyList<string>? AdmissionDiagnoses,
    [property: JsonPropertyName("dischargeDiagnoses")] IReadOnlyList<string>? DischargeDiagnoses,
    [property: JsonPropertyName("procedures")] IReadOnlyList<string>? Procedures,
    [property: JsonPropertyName("additionalDocumentContent")] string? AdditionalDocumentContent);

/// <summary>
/// 病例录入响应：返回建出的病例引用与本次推荐结果统计。
/// </summary>
public sealed record CaseEntryResponse(
    [property: JsonPropertyName("patientId")] Guid PatientId,
    [property: JsonPropertyName("visitId")] Guid VisitId,
    [property: JsonPropertyName("documentId")] Guid DocumentId,
    [property: JsonPropertyName("codingTaskId")] Guid CodingTaskId,
    [property: JsonPropertyName("pipelineVersion")] string PipelineVersion,
    [property: JsonPropertyName("admissionCount")] int AdmissionCount,
    [property: JsonPropertyName("codingStage")] string CodingStage,
    [property: JsonPropertyName("diagnosisInputs")] IReadOnlyList<DiagnosisInputResponse> DiagnosisInputs,
    [property: JsonPropertyName("recommendationCount")] int RecommendationCount);

/// <summary>
/// 审核动作请求（accept / reject / modify 共用）。
/// </summary>
public sealed record ReviewActionRequest(
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("reviewerId")] string? ReviewerId);

/// <summary>
/// 审核修改请求：修改后的编码与引用证据。
/// </summary>
public sealed record ReviewModifyRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("resultType")] string ResultType,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("reviewerId")] string? ReviewerId);

/// <summary>
/// 审核动作响应。
/// </summary>
public sealed record ReviewActionResponse(
    [property: JsonPropertyName("recommendationId")] Guid RecommendationId,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("finalCodingCount")] int FinalCodingCount);

/// <summary>
/// 最终编码提交请求。已确认的 Final Coding 不允许 AI 自动覆盖。
/// </summary>
public sealed record FinalResultSubmitRequest(
    [property: JsonPropertyName("codingTaskId")] Guid? CodingTaskId,
    [property: JsonPropertyName("diagnosisInputId")] Guid? DiagnosisInputId,
    [property: JsonPropertyName("items")] IReadOnlyList<FinalResultItem> Items);

/// <summary>
/// 最终编码项。
/// </summary>
public sealed record FinalResultItem(
    [property: JsonPropertyName("resultType")] string ResultType,
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("sourceRecommendationId")] Guid? SourceRecommendationId);

/// <summary>
/// 最终编码提交响应。
/// </summary>
public sealed record FinalResultSubmitResponse(
    [property: JsonPropertyName("codingTaskId")] Guid CodingTaskId,
    [property: JsonPropertyName("finalCodingCount")] int FinalCodingCount,
    [property: JsonPropertyName("confirmedAt")] DateTimeOffset ConfirmedAt);

/// <summary>
/// 就诊级编码视图：诊断输入、推荐摘要与最终结果。
/// </summary>
public sealed record VisitCodingResponse(
    [property: JsonPropertyName("visitId")] Guid VisitId,
    [property: JsonPropertyName("codingTaskId")] Guid? CodingTaskId,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("pipelineVersion")] string? PipelineVersion,
    [property: JsonPropertyName("diagnosisInputs")] IReadOnlyList<DiagnosisInputResponse> DiagnosisInputs,
    [property: JsonPropertyName("recommendations")] IReadOnlyList<RecommendationDetailResponse> Recommendations,
    [property: JsonPropertyName("finalResults")] IReadOnlyList<FinalCodingResponse> FinalResults);

/// <summary>
/// 最终编码结果响应。
/// </summary>
public sealed record FinalCodingResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("resultType")] string ResultType,
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("sourceRecommendationId")] Guid? SourceRecommendationId,
    [property: JsonPropertyName("reviewerId")] string? ReviewerId,
    [property: JsonPropertyName("confirmedAt")] DateTimeOffset ConfirmedAt);

/// <summary>
/// 病案文书摘要响应。
/// </summary>
public sealed record VisitDocumentResponse(
    [property: JsonPropertyName("documentId")] Guid DocumentId,
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("documentStatus")] string DocumentStatus,
    [property: JsonPropertyName("parseVersion")] string? ParseVersion,
    [property: JsonPropertyName("ocrVersion")] string? OcrVersion,
    [property: JsonPropertyName("contentHash")] string ContentHash,
    [property: JsonPropertyName("isCurrent")] bool IsCurrent,
    [property: JsonPropertyName("chunkCount")] int ChunkCount,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);
