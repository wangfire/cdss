using System.Security.Claims;
using HospitalAi.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace HospitalAi.Api.Security;

/// <summary>
/// RBAC 策略：按令牌中的权限码判定。
/// 功能权限（coding.review / user.manage）由按钮权限隐含：只授按钮的角色也能进入对应页面。
/// 缺少权限版本声明的旧令牌按原角色码口径回退，避免升级瞬间锁死已登录会话与工具脚本。
/// </summary>
public static class RbacPolicies
{
    public const string AuthenticatedUser = "authenticated-user";

    /// <summary>编码查询：就绪状态、事实、诊断输入、推荐明细。审核权限隐含读取。</summary>
    public const string CodingReader = "coding-reader";

    /// <summary>编码审核：推荐审核、诊断录入与最终编码提交。任一审核按钮权限隐含审核功能。</summary>
    public const string CodingReviewer = "coding-reviewer";

    /// <summary>知识库查看：数据浏览或任一知识库按钮权限即可进入页面。</summary>
    public const string KnowledgeReader = "knowledge-reader";

    /// <summary>用户与角色管理：功能权限或任一账户按钮权限。</summary>
    public const string UserAdmin = "user-admin";

    /// <summary>知识库按钮级权限：按具体权限码生成策略名，供各写端点复用。</summary>
    public static string PermissionPolicy(string permission) => $"permission:{permission}";

    private static readonly string[] CodingReviewActionPermissions =
    [
        AppPermissions.CodingReviewAccept,
        AppPermissions.CodingReviewModify,
        AppPermissions.CodingReviewReject
    ];

    private static readonly string[] KnowledgeActionPermissions =
    [
        AppPermissions.KnowledgeHospitalCreate,
        AppPermissions.KnowledgeIndexRebuild,
        AppPermissions.KnowledgeCodeSystemImport,
        AppPermissions.KnowledgeRulesImport,
        AppPermissions.KnowledgeIcdImport,
        AppPermissions.KnowledgeIcdCreate,
        AppPermissions.KnowledgeIcdUpdate,
        AppPermissions.KnowledgeIcdDelete,
        AppPermissions.KnowledgeCm3Import,
        AppPermissions.KnowledgeCm3Create,
        AppPermissions.KnowledgeCm3Update,
        AppPermissions.KnowledgeCm3Delete,
        AppPermissions.KnowledgeTumorCreate,
        AppPermissions.KnowledgeTumorUpdate,
        AppPermissions.KnowledgeTumorDelete,
        AppPermissions.KnowledgeSynonymImport,
        AppPermissions.KnowledgeSynonymCreate,
        AppPermissions.KnowledgeSynonymUpdate,
        AppPermissions.KnowledgeSynonymDelete
    ];

    private static readonly string[] UserActionPermissions =
    [
        AppPermissions.UserCreate,
        AppPermissions.UserStatus,
        AppPermissions.UserRoleAssign,
        AppPermissions.RoleCreate,
        AppPermissions.RolePermission
    ];

    public static IServiceCollection AddHospitalAiAuthorization(
        this IServiceCollection services)
    {
        var builder = services.AddAuthorizationBuilder()
            .AddPolicy(
                AuthenticatedUser,
                policy => policy.RequireAssertion(context =>
                    context.User.Identity?.IsAuthenticated == true))
            .AddPolicy(
                CodingReader,
                policy => policy.RequireAssertion(context =>
                    HasPermission(context.User, AppPermissions.CodingRead)
                    // 审核需要先看到推荐明细与任务详情，否则 coding.review 无法独立使用。
                    || HasPermission(context.User, AppPermissions.CodingReview)
                    || CodingReviewActionPermissions.Any(permission =>
                        HasPermission(context.User, permission))))
            .AddPolicy(
                CodingReviewer,
                policy => policy.RequireAssertion(context =>
                    HasPermission(context.User, AppPermissions.CodingReview)
                    // 只授单个审核按钮的角色，也能调用不属于具体按钮的审核辅助端点。
                    || CodingReviewActionPermissions.Any(permission =>
                        HasPermission(context.User, permission))))
            .AddPolicy(
                KnowledgeReader,
                policy => policy.RequireAssertion(context =>
                    HasPermission(context.User, AppPermissions.KnowledgeRead)
                    || HasPermission(context.User, AppPermissions.KnowledgeWrite)
                    // 只授按钮权限的角色也要能进入知识库页面查看列表。
                    || KnowledgeActionPermissions.Any(permission =>
                        HasPermission(context.User, permission))))
            .AddPolicy(
                UserAdmin,
                policy => policy.RequireAssertion(context =>
                    HasPermission(context.User, AppPermissions.UserManage)
                    || UserActionPermissions.Any(permission =>
                        HasPermission(context.User, permission))));

        // 按钮级权限与功能权限共用同一套判定（含旧令牌的角色码回退）。
        foreach (var permission in AppPermissions.All)
        {
            builder.AddPolicy(
                PermissionPolicy(permission),
                policy => policy.RequireAssertion(context =>
                    HasPermission(context.User, permission)));
        }

        return services;
    }

    /// <summary>供端点内做请求体相关的细粒度校验（如审核动作类型）。</summary>
    public static bool HasPermission(ClaimsPrincipal user, string permission)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (!user.HasClaim(claim => claim.Type == AppClaimTypes.PermissionVersion))
        {
            return HasLegacyRolePermission(user, permission);
        }

        return user.HasClaim(AppClaimTypes.Permission, permission);
    }

    private static bool HasLegacyRolePermission(ClaimsPrincipal user, string permission)
    {
        return permission switch
        {
            AppPermissions.CodingRead => !user.IsInRole("GUEST"),
            AppPermissions.KnowledgeRead => !user.IsInRole("GUEST"),
            AppPermissions.CodingReview => IsLegacyReviewer(user),
            AppPermissions.CodingReviewAccept => IsLegacyReviewer(user),
            AppPermissions.CodingReviewModify => IsLegacyReviewer(user),
            AppPermissions.CodingReviewReject => IsLegacyReviewer(user),
            AppPermissions.CodingCaseEntry => IsLegacyReviewer(user),
            AppPermissions.KnowledgeWrite => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeHospitalCreate => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeIndexRebuild => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeCodeSystemImport => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeRulesImport => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeIcdImport => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeIcdCreate => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeIcdUpdate => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeIcdDelete => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeCm3Import => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeCm3Create => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeCm3Update => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeCm3Delete => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeTumorCreate => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeTumorUpdate => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeTumorDelete => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeSynonymImport => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeSynonymCreate => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeSynonymUpdate => IsLegacyKnowledgeManager(user),
            AppPermissions.KnowledgeSynonymDelete => IsLegacyKnowledgeManager(user),
            AppPermissions.UserManage => user.IsInRole("ADMIN"),
            AppPermissions.UserCreate => user.IsInRole("ADMIN"),
            AppPermissions.UserStatus => user.IsInRole("ADMIN"),
            AppPermissions.UserRoleAssign => user.IsInRole("ADMIN"),
            AppPermissions.RoleCreate => user.IsInRole("ADMIN"),
            AppPermissions.RolePermission => user.IsInRole("ADMIN"),
            _ => false
        };
    }

    private static bool IsLegacyReviewer(ClaimsPrincipal user)
    {
        return user.IsInRole("CODER")
            || user.IsInRole("DOCTOR")
            || user.IsInRole("ADMIN")
            || user.IsInRole("COLLECTOR");
    }

    private static bool IsLegacyKnowledgeManager(ClaimsPrincipal user)
    {
        return user.IsInRole("CODER") || user.IsInRole("ADMIN");
    }
}
