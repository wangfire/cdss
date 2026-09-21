using Microsoft.AspNetCore.Authorization;

namespace HospitalAi.Api.Security;

/// <summary>
/// RBAC 策略。V2.2 编码接口按“查询 / 审核 / 知识管理”三类职责拆开，
/// 医院未定义角色时退化为仅要求已认证，避免锁死本地联调。
/// </summary>
public static class RbacPolicies
{
    public const string AuthenticatedUser = "authenticated-user";

    /// <summary>编码查询：就绪状态、事实、诊断输入、推荐明细。</summary>
    public const string CodingReader = "coding-reader";

    /// <summary>编码审核：推荐审核与最终编码提交。</summary>
    public const string CodingReviewer = "coding-reviewer";

    /// <summary>知识管理：编码体系 / 规则导入与索引重建。</summary>
    public const string KnowledgeManager = "knowledge-manager";

    public static IServiceCollection AddHospitalAiAuthorization(
        this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(
                AuthenticatedUser,
                policy => policy.RequireAssertion(context =>
                    context.User.Identity?.IsAuthenticated == true))
            .AddPolicy(
                CodingReader,
                policy => policy.RequireAssertion(
                    context => context.User.Identity?.IsAuthenticated == true
                        && !context.User.IsInRole("GUEST")))
            .AddPolicy(
                CodingReviewer,
                policy => policy.RequireAssertion(
                    context => context.User.Identity?.IsAuthenticated == true
                        && (context.User.IsInRole("CODER")
                            || context.User.IsInRole("DOCTOR")
                            || context.User.IsInRole("ADMIN")
                            || context.User.IsInRole("COLLECTOR"))))
            .AddPolicy(
                KnowledgeManager,
                policy => policy.RequireAssertion(
                    context => context.User.Identity?.IsAuthenticated == true
                        && (context.User.IsInRole("CODER")
                            || context.User.IsInRole("ADMIN"))));
        return services;
    }
}
