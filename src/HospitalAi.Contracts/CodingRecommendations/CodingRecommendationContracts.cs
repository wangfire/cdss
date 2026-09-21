using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.CodingRecommendations;

/// <summary>
/// 编码推荐响应。
/// </summary>
public sealed record CodingRecommendationResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("recommendationType")] string RecommendationType,
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("confidenceScore")] decimal ConfidenceScore,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("evidences")] IReadOnlyList<RecommendationEvidenceResponse> Evidences,
    [property: JsonPropertyName("diagnosisInputId")] Guid? DiagnosisInputId = null,
    [property: JsonPropertyName("doctorDiagnosisText")] string? DoctorDiagnosisText = null,
    [property: JsonPropertyName("isPrincipal")] bool? IsPrincipal = null,
    [property: JsonPropertyName("diagnosisOrder")] int? DiagnosisOrder = null,
    [property: JsonPropertyName("diagnosisSourceType")] string? DiagnosisSourceType = null);

/// <summary>
/// 编码推荐证据响应。evidenceLevel / pipelineRunId 供前端点击问题时定位原文与运行版本。
/// </summary>
public sealed record RecommendationEvidenceResponse(
    [property: JsonPropertyName("sourceType")] string SourceType,
    [property: JsonPropertyName("sourceText")] string SourceText,
    [property: JsonPropertyName("matchText")] string MatchText,
    [property: JsonPropertyName("score")] decimal Score,
    [property: JsonPropertyName("evidenceLevel")] string? EvidenceLevel = null,
    [property: JsonPropertyName("pipelineRunId")] Guid? PipelineRunId = null,
    [property: JsonPropertyName("documentSectionId")] Guid? DocumentSectionId = null);

/// <summary>
/// 编码任务推荐列表响应。
/// </summary>
public sealed record CodingTaskRecommendationsResponse(
    [property: JsonPropertyName("taskId")] Guid TaskId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("recommendations")] IReadOnlyList<CodingRecommendationResponse> Recommendations);

/// <summary>
/// 人工审核请求。
/// </summary>
public sealed record ReviewCodingTaskRequest(
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("finalCodes")] IReadOnlyList<ReviewedCodeItem> FinalCodes,
    [property: JsonPropertyName("comment")] string? Comment);

/// <summary>
/// 人工确认后的最终编码项。
/// </summary>
public sealed record ReviewedCodeItem(
    [property: JsonPropertyName("recommendationId")] Guid? RecommendationId,
    [property: JsonPropertyName("resultType")] string ResultType,
    [property: JsonPropertyName("codeSystem")] string CodeSystem,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title);

/// <summary>
/// 人工审核响应。
/// </summary>
public sealed record CodingReviewResponse(
    [property: JsonPropertyName("taskId")] Guid TaskId,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("finalCodeCount")] int FinalCodeCount);

/// <summary>
/// 审核工作台任务摘要。
/// </summary>
public sealed record WorkbenchTaskResponse(
    [property: JsonPropertyName("taskId")] Guid TaskId,
    [property: JsonPropertyName("visitId")] Guid VisitId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("recommendationCount")] int RecommendationCount,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("patientName")] string? PatientName,
    [property: JsonPropertyName("medicalRecordNo")] string? MedicalRecordNo,
    [property: JsonPropertyName("admissionCount")] int AdmissionCount,
    [property: JsonPropertyName("dischargeAt")] DateTimeOffset? DischargeAt);
