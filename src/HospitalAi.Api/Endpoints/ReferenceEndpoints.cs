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
    /// <summary>
    /// 医院公开列表的内存缓存（60 秒）：匿名端点防刷。
    /// 医院列表变更频率极低（管理端手工建），过期后下次请求重建。
    /// </summary>
    private static readonly object HospitalCacheLock = new();
    private static IReadOnlyList<object>? _hospitalCache;
    private static DateTimeOffset _hospitalCacheAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan HospitalCacheLifetime = TimeSpan.FromSeconds(60);

    /// <summary>读取医院列表（带 60 秒内存缓存）。传入活动中的 DbContext 仅在缓存失效时使用。</summary>
    internal static async Task<IReadOnlyList<object>> GetOrAddAsync(
        HospitalAiDbContext db,
        CancellationToken cancellationToken)
    {
        if (_hospitalCache is not null && DateTimeOffset.UtcNow - _hospitalCacheAt < HospitalCacheLifetime)
        {
            return _hospitalCache;
        }

        List<object> items;
        lock (HospitalCacheLock)
        {
            // 双检：等锁期间可能已被其他请求重建。
            if (_hospitalCache is not null && DateTimeOffset.UtcNow - _hospitalCacheAt < HospitalCacheLifetime)
            {
                return _hospitalCache;
            }
        }

        items = await db.Hospitals
            .AsNoTracking()
            .OrderBy(item => item.Code)
            .Select(item => new { item.Id, item.Code, item.Name })
            .Cast<object>()
            .ToListAsync(cancellationToken);

        lock (HospitalCacheLock)
        {
            _hospitalCache = items;
            _hospitalCacheAt = DateTimeOffset.UtcNow;
        }

        return items;
    }
    /// <summary>MapReferenceEndpoints（由 Program.cs 搬移）。</summary>
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        // 医院公开列表（WP-A 医院下拉登录）：匿名可访问，仅返回 id/code/name 三字段，
        // 60 秒内存缓存防刷。供登录页在未认证状态下选择医院。
        app.MapGet(
            "/api/v1/hospitals/public",
            async (HospitalAiDbContext db, CancellationToken cancellationToken) =>
            {
                // 内存缓存：匿名端点防刷（兑现此前注释宣称的 60 秒缓存）。
                // 医院列表变更频率极低（管理端手工建），60 秒延迟可接受；
                // 新建医院后最多 60 秒出现在登录页下拉。
                var items = await GetOrAddAsync(db, cancellationToken);
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