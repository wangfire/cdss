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
/// 用户列表项：一行一个用户，附带其当前角色代码。
/// </summary>
public sealed record AppUserSummaryResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("lastLoginAt")] DateTimeOffset? LastLoginAt,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles);

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
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);
