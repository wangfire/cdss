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
/// Auth 相关端点。由 Program.cs 拆分而来（纯代码搬移，行为不变）。
/// </summary>
public static class AuthEndpoints
{
    /// <summary>MapAuthEndpoints（由 Program.cs 搬移）。</summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // ===== 认证端点（前端 authApi 对齐 /api/v1/auth/*）=====
        // 登录请求体含 hospitalId（前端不注入 X-Hospital-Id 头时也能确定医院范围）。
        app.MapPost(
            "/api/v1/auth/login",
            async (
                LoginRequest request,
                IAuthService authService,
                CancellationToken cancellationToken) =>
            {
                var response = await authService.LoginAsync(request, cancellationToken);
                return Results.Ok(ApiEnvelope.Create(response));
            }).RequireRateLimiting("login");

        app.MapGet(
            "/api/v1/auth/me",
            async (
                IAuthService authService,
                CancellationToken cancellationToken) =>
            {
                var response = await authService.GetCurrentUserAsync(cancellationToken);
                return Results.Ok(ApiEnvelope.Create(response));
            }).RequireAuthorization();

        app.MapPost(
            "/api/v1/auth/logout",
            () =>
            {
                // 第一阶段：JWT 短期令牌 + 客户端清除；无服务端黑名单。
                return Results.Ok(ApiEnvelope.Create(new { ok = true }));
            });

        app.MapPost(
            "/api/v1/auth/change-password",
            async (
                ChangePasswordRequest request,
                IAuthService authService,
                CancellationToken cancellationToken) =>
            {
                await authService.ChangePasswordAsync(request, cancellationToken);
                return Results.Ok(ApiEnvelope.Create(new { ok = true }));
            }).RequireAuthorization();

        return app;
    }
}