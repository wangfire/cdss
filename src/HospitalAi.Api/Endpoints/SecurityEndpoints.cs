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
/// Security 相关端点。由 Program.cs 拆分而来（纯代码搬移，行为不变）。
/// </summary>
public static class SecurityEndpoints
{
    /// <summary>MapSecurityEndpoints（由 Program.cs 搬移）。</summary>
    public static IEndpointRouteBuilder MapSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/users",
            async (
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                var users = await service.ListUsersAsync(cancellationToken);
                return Results.Ok(ApiEnvelope.Create(users));
            }).RequireAuthorization(RbacPolicies.UserAdmin);

        app.MapPost(
            "/api/v1/users",
            async (
                CreateUserRequest request,
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.CreateUserAsync(request, cancellationToken);
                return Results.Created($"/api/v1/users/{response.Id}", ApiEnvelope.Create(response));
            }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserCreate));

        app.MapGet(
            "/api/v1/roles",
            async (
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                var roles = await service.ListRolesAsync(cancellationToken);
                return Results.Ok(ApiEnvelope.Create(roles));
            }).RequireAuthorization(RbacPolicies.UserAdmin);

        app.MapPost(
            "/api/v1/roles",
            async (
                CreateRoleRequest request,
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.CreateRoleAsync(request, cancellationToken);
                return Results.Created($"/api/v1/roles/{response.Id}", ApiEnvelope.Create(response));
            }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.RoleCreate));

        app.MapPost(
            "/api/v1/users/{userId:guid}/roles/{roleId:guid}",
            async (
                Guid userId,
                Guid roleId,
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                await service.AssignRoleAsync(userId, roleId, cancellationToken);
                return Results.Ok(ApiEnvelope.Create(new { ok = true }));
            }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserRoleAssign));

        app.MapDelete(
            "/api/v1/users/{userId:guid}/roles/{roleId:guid}",
            async (
                Guid userId,
                Guid roleId,
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                await service.RemoveRoleAsync(userId, roleId, cancellationToken);
                return Results.Ok(ApiEnvelope.Create(new { ok = true }));
            }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserRoleAssign));

        app.MapGet(
            "/api/v1/users/{userId:guid}/roles",
            async (
                Guid userId,
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                var roles = await service.GetRolesByUserAsync(userId, cancellationToken);
                return Results.Ok(ApiEnvelope.Create(roles));
            }).RequireAuthorization(RbacPolicies.UserAdmin);

        app.MapPatch(
            "/api/v1/users/{userId:guid}/status",
            async (
                Guid userId,
                UpdateUserStatusRequest request,
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                await service.SetUserStatusAsync(userId, request.Status, cancellationToken);
                return Results.Ok(ApiEnvelope.Create(new { ok = true }));
            }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserStatus));

        // 解除登录锁定（防暴力破解运维动作）：清零失败计数并解除锁定，与停用/启用同级权限。
        app.MapPost(
            "/api/v1/users/{userId:guid}/unlock",
            async (
                Guid userId,
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                await service.UnlockUserAsync(userId, cancellationToken);
                return Results.Ok(ApiEnvelope.Create(new { ok = true }));
            }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserStatus));

        // 内置角色模板（正式权限矩阵）：创建角色时一键套用的授权口径，只读、账户管理面可见。
        app.MapGet(
            "/api/v1/security/role-templates",
            async (
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                var templates = await service.ListRoleTemplatesAsync(cancellationToken);
                return Results.Ok(ApiEnvelope.Create(templates));
            }).RequireAuthorization(RbacPolicies.UserAdmin);

        // 审计日志查询（合规闭环）：只有能查的审计才算真正的审计。
        // 归为账户管理员的职责（user.manage 权限族）——审计查看是运营/合规动作，
        // 不引入新权限码即可复用既有角色矩阵；日志本身不含患者临床数据。
        app.MapGet(
            "/api/v1/audit-logs",
            async (
                HospitalAiDbContext db,
                IRequestContext requestContext,
                int? page,
                int? pageSize,
                string? resourceType,
                string? action,
                string? actorId,
                CancellationToken cancellationToken) =>
            {
                if (requestContext.HospitalId == Guid.Empty)
                {
                    return Results.BadRequest(ApiEnvelope.Create<object>(new { error = "HOSPITAL_CONTEXT_REQUIRED" }));
                }

                var query = db.AuditLogs
                    .AsNoTracking()
                    .Where(item => item.HospitalId == requestContext.HospitalId);
                if (!string.IsNullOrWhiteSpace(resourceType))
                {
                    query = query.Where(item => item.ResourceType == resourceType);
                }

                if (!string.IsNullOrWhiteSpace(action))
                {
                    query = query.Where(item => item.Action.StartsWith(action, StringComparison.Ordinal));
                }

                if (!string.IsNullOrWhiteSpace(actorId))
                {
                    query = query.Where(item => item.ActorId == actorId);
                }

                var total = await query.CountAsync(cancellationToken);
                var pageNum = Math.Max(page ?? 1, 1);
                var size = Math.Clamp(pageSize ?? 20, 1, 200);
                var items = await query
                    .OrderByDescending(item => item.CreatedAt)
                    .ThenByDescending(item => item.Id)
                    .Skip((pageNum - 1) * size)
                    .Take(size)
                    .Select(item => new
                    {
                        item.Id,
                        item.ResourceType,
                        item.ResourceId,
                        item.Action,
                        item.Result,
                        item.ActorId,
                        item.RequestId,
                        item.CreatedAt
                    })
                    .ToListAsync(cancellationToken);

                return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
            }).RequireAuthorization(RbacPolicies.UserAdmin);

        app.MapGet(
            "/api/v1/roles/{roleId:guid}/permissions",
            async (
                Guid roleId,
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                var permissions = await service.GetRolePermissionsAsync(roleId, cancellationToken);
                return Results.Ok(ApiEnvelope.Create(permissions));
            }).RequireAuthorization(RbacPolicies.UserAdmin);

        app.MapPut(
            "/api/v1/roles/{roleId:guid}/permissions",
            async (
                Guid roleId,
                UpdateRolePermissionsRequest request,
                ISecurityService service,
                CancellationToken cancellationToken) =>
            {
                await service.SetRolePermissionsAsync(roleId, request.Permissions, cancellationToken);
                return Results.Ok(ApiEnvelope.Create(new { ok = true }));
            }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.RolePermission));

        // ===== V2.2-Lite 新增接口 =====
        // 约定：ApiEnvelope 包装；RequestContext 提供医院隔离；
        // 默认只返回当前 PipelineVersion 结果，Legacy 必须显式 includeLegacy=true；

        return app;
    }
}