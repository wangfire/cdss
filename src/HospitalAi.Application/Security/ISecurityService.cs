using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Security;
using HospitalAi.Contracts.Security;

namespace HospitalAi.Application.Security;

/// <summary>
/// RBAC 用户/角色管理契约。权限按角色配置，用户下次登录后生效。
/// </summary>
public interface ISecurityService
{
    Task<AppUserResponse> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppUserSummaryResponse>> ListUsersAsync(
        CancellationToken cancellationToken = default);

    Task<AppRoleResponse> CreateRoleAsync(
        CreateRoleRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppRoleResponse>> ListRolesAsync(
        CancellationToken cancellationToken = default);

    Task AssignRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task RemoveRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppRoleResponse>> GetRolesByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task SetUserStatusAsync(
        Guid userId,
        string status,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRolePermissionsAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task SetRolePermissionsAsync(
        Guid roleId,
        IReadOnlyList<string> permissionCodes,
        CancellationToken cancellationToken = default);

    /// <summary>解锁登录锁定的用户：清零失败计数并解除锁定（管理员防暴力破解运维动作）。</summary>
    Task UnlockUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>内置角色模板（正式权限矩阵）：创建角色时可整体套用。</summary>
    Task<IReadOnlyList<RoleTemplateResponse>> ListRoleTemplatesAsync(
        CancellationToken cancellationToken = default);
}
