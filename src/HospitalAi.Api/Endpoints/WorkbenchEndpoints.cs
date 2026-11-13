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
/// Workbench 相关端点。由 Program.cs 拆分而来（纯代码搬移，行为不变）。
/// </summary>
public static class WorkbenchEndpoints
{
    /// <summary>MapWorkbenchEndpoints（由 Program.cs 搬移）。</summary>
    public static IEndpointRouteBuilder MapWorkbenchEndpoints(this IEndpointRouteBuilder app)
    {
    app.MapGet(
        "/api/v1/coding-tasks/{taskId:guid}/recommendations",
        async (
            Guid taskId,
            bool? includeLegacy,
            ICodingRecommendationService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.GetRecommendationsAsync(
                taskId,
                includeLegacy ?? false,
                cancellationToken);
            return response is null
                ? Results.NotFound(ApiEnvelope.Create(new { message = "资源不存在或已被删除。" }))
                : Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReader);

    app.MapPost(
        "/api/v1/coding-tasks/{taskId:guid}/review",
        async (
            Guid taskId,
            ReviewCodingTaskRequest request,
            ICodingRecommendationService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            // 审核动作按按钮权限逐个校验：父权限 coding.review 视为全量审核权限。
            var requiredPermission = request.ReviewStatus?.Trim().ToUpperInvariant() switch
            {
                "ACCEPTED" => AppPermissions.CodingReviewAccept,
                "MODIFIED" => AppPermissions.CodingReviewModify,
                "REJECTED" => AppPermissions.CodingReviewReject,
                _ => null
            };
            if (requiredPermission is not null
                && !RbacPolicies.HasPermission(httpContext.User, requiredPermission)
                && !RbacPolicies.HasPermission(httpContext.User, AppPermissions.CodingReview))
            {
                return Results.Forbid();
            }

            var response = await service.ReviewAsync(taskId, request, cancellationToken);
            return Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReviewer);

    app.MapGet(
        "/api/v1/workbench/tasks",
        async (
            string? status,
            string? keyword,
            int? page,
            int? pageSize,
            ICodingRecommendationService service,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ListWorkbenchTasksPageAsync(
                status, keyword, page ?? 1, pageSize ?? 20, cancellationToken);
            return Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReader);

    // ===== 流水线运行时配置（工作台设置抽屉，WP-A 前端生产化） =====

    // 流水线配置响应：三个开关 + 最近更新时间。

    // 工作台任务状态统计（仪表盘统计卡）：数据库端 GROUP BY，无全量拉取。
    app.MapGet(
        "/api/v1/workbench/tasks/stats",
        async (ICodingRecommendationService service, CancellationToken cancellationToken) =>
        {
            var response = await service.GetWorkbenchTaskStatsAsync(cancellationToken);
            return Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.CodingReader);

    // 说明：早期版本的 /workbench 服务端渲染 HTML 页已移除。
    // 它用 innerHTML 直接拼接推荐标题与证据原文（来自病历文书，含临床文本），
    // 存在存储型 XSS；且为匿名端点。功能由 web-ui 的 /workbench 路由页（Vue）完整承接，
    // 审核入口统一为前端地址 http://localhost:3001/workbench。

        return app;
    }
}