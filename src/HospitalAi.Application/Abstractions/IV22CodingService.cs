using HospitalAi.Contracts.CodingRecommendations;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// V2.2-Lite 编码应用服务：就绪检查、临床事实、诊断输入、推荐查询、审核闸门与最终编码提交。
/// 所有查询必须按 RequestContext.HospitalId 隔离，不允许跨医院访问，不允许修改 Legacy 结果。
/// </summary>
public interface IV22CodingService
{
    Task<VisitReadinessResponse?> GetVisitReadinessAsync(
        Guid visitId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClinicalFactResponse>> GetClinicalFactsAsync(
        Guid visitId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VisitDocumentResponse>> GetVisitDocumentsAsync(
        Guid visitId,
        CancellationToken cancellationToken = default);

    Task<VisitCodingResponse?> GetVisitCodingAsync(
        Guid visitId,
        bool includeLegacy = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiagnosisInputResponse>> SubmitDiagnosisInputsAsync(
        Guid taskId,
        DiagnosisInputBatchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>对诊断输入执行 V2.2-Lite 推荐流水线。</summary>
    Task<RecommendationDetailResponse?> RecommendAsync(
        Guid diagnosisInputId,
        RecommendRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>病例录入：建患者 / 就诊 / 文书 / 任务与诊断输入，并同步跑一遍 V2.2-Lite 推荐。</summary>
    Task<CaseEntryResponse> CreateCaseEntryAsync(
        CaseEntryRequest request,
        CancellationToken cancellationToken = default);

    Task<RecommendationListResponse?> GetDiagnosisInputRecommendationsAsync(
        Guid diagnosisInputId,
        bool includeLegacy = false,
        CancellationToken cancellationToken = default);

    Task<RecommendationDetailResponse?> GetRecommendationAsync(
        Guid recommendationId,
        CancellationToken cancellationToken = default);

    Task<ReviewActionResponse?> AcceptRecommendationAsync(
        Guid recommendationId,
        ReviewActionRequest request,
        CancellationToken cancellationToken = default);

    Task<ReviewActionResponse?> RejectRecommendationAsync(
        Guid recommendationId,
        ReviewActionRequest request,
        CancellationToken cancellationToken = default);

    Task<ReviewActionResponse?> ModifyRecommendationAsync(
        Guid recommendationId,
        ReviewModifyRequest request,
        CancellationToken cancellationToken = default);

    Task<FinalResultSubmitResponse> SubmitFinalResultsAsync(
        FinalResultSubmitRequest request,
        CancellationToken cancellationToken = default);
}
