using HospitalAi.Api;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Worker.Pipeline;
using HospitalAi.Infrastructure.Outbox;
using HospitalAi.Api.Observability;
using HospitalAi.Api.Security;
using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Application.Coding.NameLookup;
using HospitalAi.Application.Security;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Contracts.CodingKnowledge;
using HospitalAi.Contracts.CodingRecommendations;
using HospitalAi.Contracts.Documents;
using HospitalAi.Contracts.Hospitals;
using HospitalAi.Contracts.Patients;
using HospitalAi.Contracts.Security;
using HospitalAi.Contracts.Visits;
using HospitalAi.Infrastructure.CodingKnowledge;
using HospitalAi.Infrastructure.CodingLookup;
using HospitalAi.Infrastructure.CodingRecommendations;
using HospitalAi.Infrastructure.CodingTasks;
using HospitalAi.Infrastructure.Observability;
using HospitalAi.Infrastructure.ReferenceData;
using HospitalAi.Infrastructure.RabbitMq;
using HospitalAi.Infrastructure.Security;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Infrastructure.Tracing;
using HospitalAi.Worker;
using OpenTelemetry.Exporter.Prometheus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using System.Threading.RateLimiting;

namespace HospitalAi.Api.Endpoints;

/// <summary>
/// Coding 相关端点。由 Program.cs 拆分而来（纯代码搬移，行为不变）。
/// </summary>
public static class CodingEndpoints
{
    /// <summary>MapCodingEndpoints（由 Program.cs 搬移）。</summary>
    public static IEndpointRouteBuilder MapCodingEndpoints(this IEndpointRouteBuilder app)
    {
    app.MapPost(
        "/api/v1/coding/case-entry",
        async (
            CaseEntryRequest request,
            IV22CodingService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.CreateCaseEntryAsync(request, cancellationToken);
            return Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.CodingCaseEntry));

    // ===== "编码录入"：诊断/手术名称 → 主导词全量匹配拆分 → 修饰词下钻 → 有效编码推荐 =====
    app.MapPost(
        "/api/v1/coding/name-lookup",
        async (
            NameLookupRequest request,
            INameCodingLookupService service,
            CancellationToken cancellationToken) =>
        {
            var kind = request.Kind?.Trim().ToLowerInvariant();
            if (kind is not (NameCodingLookupEngine.DiagnosisKind or NameCodingLookupEngine.ProcedureKind))
            {
                return Results.BadRequest(ApiEnvelope.Create<object>(new { error = "INVALID_KIND" }));
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest(ApiEnvelope.Create<object>(new { error = "EMPTY_TEXT" }));
            }

            var response = await service.LookupAsync(
                request with { Kind = kind, Text = request.Text.Trim() },
                cancellationToken);
            return Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.CodingCaseEntry));

    app.MapPost(
        "/api/v1/coding/tasks/{taskId:guid}/diagnoses:batch",
        async (
            Guid taskId,
            DiagnosisInputBatchRequest request,
            IV22CodingService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.SubmitDiagnosisInputsAsync(taskId, request, cancellationToken);
            return Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReviewer);

    app.MapPost(
        "/api/v1/coding/diagnosis-inputs/{diagnosisInputId:guid}/recommend",
        async (
            Guid diagnosisInputId,
            RecommendRequest? request,
            IV22CodingService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.RecommendAsync(
                diagnosisInputId,
                request ?? new RecommendRequest(DiagnosisInputIds: null),
                cancellationToken);
            return response is null
                ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "RECOMMENDATION_NOT_FOUND" }))
                : Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReviewer);

    app.MapGet(
        "/api/v1/coding/diagnosis-inputs/{diagnosisInputId:guid}/recommendations",
        async (
            Guid diagnosisInputId,
            bool? includeLegacy,
            IV22CodingService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.GetDiagnosisInputRecommendationsAsync(
                diagnosisInputId,
                includeLegacy ?? false,
                cancellationToken);
            return response is null
                ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "DIAGNOSIS_INPUT_NOT_FOUND" }))
                : Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReader);

    app.MapGet(
        "/api/v1/coding/recommendations/{recommendationId:guid}",
        async (
            Guid recommendationId,
            IV22CodingService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.GetRecommendationAsync(recommendationId, cancellationToken);
            return response is null
                ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "RECOMMENDATION_NOT_FOUND" }))
                : Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReader);

    app.MapPost(
        "/api/v1/reviews/{recommendationId:guid}/accept",
        async (
            Guid recommendationId,
            ReviewActionRequest? request,
            IV22CodingService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.AcceptRecommendationAsync(
                recommendationId,
                request ?? new ReviewActionRequest(Comment: null, ReviewerId: null),
                cancellationToken);
            return response is null
                ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "RECOMMENDATION_NOT_FOUND" }))
                : Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.CodingReviewAccept));

    app.MapPost(
        "/api/v1/reviews/{recommendationId:guid}/reject",
        async (
            Guid recommendationId,
            ReviewActionRequest? request,
            IV22CodingService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.RejectRecommendationAsync(
                recommendationId,
                request ?? new ReviewActionRequest(Comment: null, ReviewerId: null),
                cancellationToken);
            return response is null
                ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "RECOMMENDATION_NOT_FOUND" }))
                : Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.CodingReviewReject));

    app.MapPost(
        "/api/v1/reviews/{recommendationId:guid}/modify",
        async (
            Guid recommendationId,
            ReviewModifyRequest request,
            IV22CodingService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ModifyRecommendationAsync(recommendationId, request, cancellationToken);
            return response is null
                ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "RECOMMENDATION_NOT_FOUND" }))
                : Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.CodingReviewModify));

    app.MapPost(
        "/api/v1/final-results/submit",
        async (
            FinalResultSubmitRequest request,
            IV22CodingService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.SubmitFinalResultsAsync(request, cancellationToken);
            return Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReviewer);

        return app;
    }
}