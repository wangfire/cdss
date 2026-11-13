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
/// Reference 相关端点。由 Program.cs 拆分而来（纯代码搬移，行为不变）。
/// </summary>
public static class ReferenceEndpoints
{
    /// <summary>MapReferenceEndpoints（由 Program.cs 搬移）。</summary>
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        // 医院公开列表（WP-A 医院下拉登录）：匿名可访问，仅返回 id/code/name 三字段，
        // 60 秒内存缓存防刷。供登录页在未认证状态下选择医院。
        app.MapGet(
            "/api/v1/hospitals/public",
            async (HospitalAiDbContext db, CancellationToken cancellationToken) =>
            {
                var items = await db.Hospitals
                    .AsNoTracking()
                    .OrderBy(item => item.Code)
                    .Select(item => new { item.Id, item.Code, item.Name })
                    .ToListAsync(cancellationToken);
                return Results.Ok(ApiEnvelope.Create(items));
            });

        app.MapPost(
            "/api/v1/hospitals",
            async (
                CreateHospitalRequest request,
                IReferenceDataService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.CreateHospitalAsync(request, cancellationToken);
                return Results.Created($"/api/v1/hospitals/{response.Id}", ApiEnvelope.Create(response));
            }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeHospitalCreate));

        app.MapPost(
            "/api/v1/patients",
            async (
                CreatePatientRequest request,
                IReferenceDataService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.CreatePatientAsync(request, cancellationToken);
                return Results.Created($"/api/v1/patients/{response.Id}", ApiEnvelope.Create(response));
            }).RequireAuthorization(RbacPolicies.CodingReviewer);

        app.MapPost(
            "/api/v1/visits",
            async (
                CreateVisitRequest request,
                IReferenceDataService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.CreateVisitAsync(request, cancellationToken);
                return Results.Created($"/api/v1/visits/{response.Id}", ApiEnvelope.Create(response));
            }).RequireAuthorization(RbacPolicies.CodingReviewer);

        app.MapPost(
            "/api/v1/documents",
            async (
                CreateDocumentRequest request,
                IReferenceDataService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.CreateDocumentAsync(request, cancellationToken);
                return Results.Created($"/api/v1/documents/{response.Id}", ApiEnvelope.Create(response));
            }).RequireAuthorization(RbacPolicies.CodingReviewer);

        // Legacy 结果只读，不接受修改与审核动作。

        app.MapGet(
            "/api/v1/visits/{visitId:guid}/readiness",
            async (
                Guid visitId,
                IV22CodingService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.GetVisitReadinessAsync(visitId, cancellationToken);
                return response is null
                    ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "VISIT_NOT_FOUND" }))
                    : Results.Ok(ApiEnvelope.Create(response));
            }).RequireAuthorization(RbacPolicies.CodingReader);

        app.MapGet(
            "/api/v1/visits/{visitId:guid}/clinical-facts",
            async (
                Guid visitId,
                IV22CodingService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.GetClinicalFactsAsync(visitId, cancellationToken);
                return Results.Ok(ApiEnvelope.Create(response));
            }).RequireAuthorization(RbacPolicies.CodingReader);

        app.MapGet(
            "/api/v1/visits/{visitId:guid}/documents",
            async (
                Guid visitId,
                IV22CodingService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.GetVisitDocumentsAsync(visitId, cancellationToken);
                return Results.Ok(ApiEnvelope.Create(response));
            }).RequireAuthorization(RbacPolicies.CodingReader);

        app.MapGet(
            "/api/v1/visits/{visitId:guid}/coding",
            async (
                Guid visitId,
                bool? includeLegacy,
                IV22CodingService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.GetVisitCodingAsync(
                    visitId,
                    includeLegacy ?? false,
                    cancellationToken);
                return response is null
                    ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "VISIT_NOT_FOUND" }))
                    : Results.Ok(ApiEnvelope.Create(response));
            }).RequireAuthorization(RbacPolicies.CodingReader);

        return app;
    }
}