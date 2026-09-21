using System.Text.Json.Serialization;

namespace HospitalAi.Contracts.Security;

/// <summary>
/// 登录请求：用户名（用户 Code）+ 密码 + 医院 Id。
/// </summary>
public sealed record LoginRequest(
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("hospitalId")] Guid HospitalId);

/// <summary>
/// 登录响应：JWT 令牌 + 用户信息 + 角色 + 权限码。
/// 字段名与前端 UserDto 对齐。
/// </summary>
public sealed record LoginResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("user")] UserDto User,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions);

/// <summary>
/// 当前用户响应（/me）。
/// </summary>
public sealed record CurrentUserResponse(
    [property: JsonPropertyName("user")] UserDto User,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions);

/// <summary>
/// 用户 DTO，字段名与前端 UserDto 完全对齐。
/// </summary>
public sealed record UserDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("avatar")] string? Avatar,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("lastLoginAt")] DateTimeOffset? LastLoginAt,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles);

/// <summary>
/// 修改密码请求。
/// </summary>
public sealed record ChangePasswordRequest(
    [property: JsonPropertyName("oldPassword")] string OldPassword,
    [property: JsonPropertyName("newPassword")] string NewPassword);
