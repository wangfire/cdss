using HospitalAi.Contracts.Security;

namespace HospitalAi.Application.Security;

/// <summary>
/// 认证服务契约：登录（用户名/密码 → JWT）、当前用户、修改密码。
/// 与前端 authApi（/api/v1/auth/*）对齐。
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// 用户名/密码登录，校验密码哈希（PBKDF2）并签发短期 JWT。
    /// </summary>
    Task<LoginResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据当前认证主体（JWT 中的 userId + hospitalId）返回用户、角色与权限码。
    /// </summary>
    Task<CurrentUserResponse> GetCurrentUserAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改当前用户密码（校验旧密码后重新哈希）。
    /// </summary>
    Task ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}
