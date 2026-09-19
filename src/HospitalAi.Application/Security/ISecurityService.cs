using HospitalAi.Application.Abstractions;
using HospitalAi.Contracts.Security;

namespace HospitalAi.Application.Security;

/// <summary>
/// RBAC 用户/角色管理契约。第一版只提供最小 RBAC 模型，完整权限矩阵待医院接入前确认。
/// </summary>
public interface ISecurityService
{
    Task<AppUserResponse> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default);

    Task<AppRoleResponse> CreateRoleAsync(
        CreateRoleRequest request,
        CancellationToken cancellationToken = default);

    Task AssignRoleAsync(
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
}
