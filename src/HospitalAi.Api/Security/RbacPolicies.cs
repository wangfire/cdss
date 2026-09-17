using Microsoft.AspNetCore.Authorization;

namespace HospitalAi.Api.Security;

/// <summary>
/// 第一阶段只注册最小 RBAC 策略，真实角色矩阵待医院接入前确认。
/// </summary>
public static class RbacPolicies
{
    public const string AuthenticatedUser = "authenticated-user";

    public static IServiceCollection AddHospitalAiAuthorization(
        this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(
                AuthenticatedUser,
                policy => policy.RequireAssertion(context =>
                    context.User.Identity?.IsAuthenticated == true));
        return services;
    }
}
