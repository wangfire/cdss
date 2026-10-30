using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.Security;

/// <summary>
/// 创建应用用户请求。
/// </summary>
public sealed record CreateUserRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("displayName")] string DisplayName);

/// <summary>
/// 应用用户响应。
/// </summary>
public sealed record AppUserResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("hospitalId")] Guid HospitalId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

/// <summary>
/// 用户列表项：一行一个用户，附带其当前角色代码与登录锁定状态。
/// </summary>
public sealed record AppUserSummaryResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("lastLoginAt")] DateTimeOffset? LastLoginAt,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("lockout")] UserLockoutDto? Lockout = null);

/// <summary>
/// 设置用户状态请求。
/// </summary>
public sealed record UpdateUserStatusRequest(
    [property: JsonPropertyName("status")] string Status);

/// <summary>
/// 创建应用角色请求。
/// </summary>
public sealed record CreateRoleRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description = null);

/// <summary>
/// 应用角色响应。
/// </summary>
public sealed record AppRoleResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("hospitalId")] Guid HospitalId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions);

/// <summary>
/// 整体替换角色权限集合。
/// </summary>
public sealed record UpdateRolePermissionsRequest(
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions);

/// <summary>
/// 用户锁定状态摘要：登录锁定（防暴力破解）的展示与解锁运维所需字段。
/// locked 为 false 时锁定时间为 null。
/// </summary>
public sealed record UserLockoutDto(
    [property: JsonPropertyName("locked")] bool Locked,
    [property: JsonPropertyName("failedLoginCount")] int FailedLoginCount,
    [property: JsonPropertyName("lockedUntil")] DateTimeOffset? LockedUntil,
    [property: JsonPropertyName("lockoutAt")] DateTimeOffset? LockoutAt);

/// <summary>
/// 内置角色模板（正式权限矩阵）：创建角色时可整体套用的授权口径。
/// </summary>
public sealed record RoleTemplateResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions);
