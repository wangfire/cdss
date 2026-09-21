using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Application.Security;
using HospitalAi.Contracts.Security;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HospitalAi.Infrastructure.Security;

/// <summary>
/// 基于 SQL Server 的认证服务实现。
/// 用户身份：app_user（含 PBKDF2 密码哈希）；角色：app_role。
/// JWT：HS256 短期令牌，声明含 sub(userId) + hospitalId。
/// </summary>
public sealed class SqlServerAuthService(
    HospitalAiDbContext dbContext,
    IRequestContext requestContext,
    IJwtOptionsProvider jwtOptionsProvider) : IAuthService
{
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string HashVersion = "v1";

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureLoginRequest(request);
        var hospitalId = request.HospitalId;

        var user = await dbContext.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.HospitalId == hospitalId && item.Code == request.UserName.Trim(),
                cancellationToken);
        if (user is null)
        {
            // 统一错误消息，避免用户枚举。
            throw new ValidationException("用户名或密码错误。");
        }

        if (!VerifyPassword(user.PasswordHash, request.Password))
        {
            throw new ValidationException("用户名或密码错误。");
        }

        if (!string.Equals(user.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("用户已被禁用，请联系管理员。");
        }

        var roles = await GetRoleCodesAsync(user.Id, hospitalId, cancellationToken);
        var permissions = await GetPermissionCodesAsync(user.Id, hospitalId, cancellationToken);

        // 更新最后登录时间（尽力而为，失败不影响登录成功）。
        try
        {
            var tracked = await dbContext.AppUsers.SingleAsync(
                item => item.Id == user.Id, cancellationToken);
            tracked.LastLoginAt = DateTimeOffset.UtcNow;
            tracked.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // 非关键路径：写失败不阻断登录。
        }

        var now = DateTimeOffset.UtcNow;
        var lifetimeMinutes = Math.Max(jwtOptionsProvider.Options.TokenLifetimeMinutes, 1);
        var expiresAt = now.AddMinutes(lifetimeMinutes);
        var token = BuildJwt(user, hospitalId, expiresAt, roles);

        return new LoginResponse(
            token,
            expiresAt,
            ToUserDto(user, roles),
            roles,
            permissions);
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        if (requestContext.HospitalId == Guid.Empty)
        {
            throw new ValidationException("X-Hospital-Id 不能为空。");
        }

        var userIdStr = requestContext.UserId;
        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            // 兼容 Header 占位认证场景：未通过 JWT 登录的旧请求。
            throw new ValidationException("未认证或令牌已过期。");
        }

        var user = await dbContext.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == userId && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (user is null)
        {
            throw new ResourceNotFoundException("当前用户不存在。");
        }

        var roles = await GetRoleCodesAsync(user.Id, requestContext.HospitalId, cancellationToken);
        var permissions = await GetPermissionCodesAsync(user.Id, requestContext.HospitalId, cancellationToken);

        return new CurrentUserResponse(
            ToUserDto(user, roles),
            roles,
            permissions);
    }

    public async Task ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (requestContext.HospitalId == Guid.Empty)
        {
            throw new ValidationException("X-Hospital-Id 不能为空。");
        }

        var userIdStr = requestContext.UserId;
        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            throw new ValidationException("未认证。");
        }

        var user = await dbContext.AppUsers
            .SingleOrDefaultAsync(
                item => item.Id == userId && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (user is null)
        {
            throw new ResourceNotFoundException("用户不存在。");
        }

        if (!VerifyPassword(user.PasswordHash, request.OldPassword))
        {
            throw new ValidationException("原密码不正确。");
        }

        if (request.NewPassword.Length < 8)
        {
            throw new ValidationException("新密码至少 8 位。");
        }

        user.PasswordHash = HashPassword(request.NewPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>> GetRoleCodesAsync(
        Guid userId, Guid hospitalId, CancellationToken ct)
    {
        var codes = await dbContext.AppUserRoles
            .AsNoTracking()
            .Where(link => link.HospitalId == hospitalId && link.AppUserId == userId)
            .Select(link => link.AppRole!.Code)
            .ToListAsync(ct);
        return codes;
    }

    private async Task<IReadOnlyList<string>> GetPermissionCodesAsync(
        Guid userId, Guid hospitalId, CancellationToken ct)
    {
        // 第一阶段：权限码 = 角色 code（角色矩阵待医院接入前确认）。
        var codes = await dbContext.AppUserRoles
            .AsNoTracking()
            .Where(link => link.HospitalId == hospitalId && link.AppUserId == userId)
            .Select(link => link.AppRole!.Code)
            .ToListAsync(ct);
        return codes;
    }

    private static UserDto ToUserDto(AppUserRecord user, IReadOnlyList<string> roles)
    {
        return new UserDto(
            Id: user.Id.ToString(),
            UserName: user.Code,
            Email: string.Empty,
            DisplayName: user.DisplayName,
            Avatar: null,
            Phone: null,
            IsActive: string.Equals(user.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase),
            CreatedAt: user.CreatedAt,
            LastLoginAt: user.LastLoginAt,
            Roles: roles);
    }

    private void EnsureLoginRequest(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            throw new ValidationException("用户名不能为空。");
        }
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ValidationException("密码不能为空。");
        }
        if (request.HospitalId == Guid.Empty)
        {
            throw new ValidationException("医院 Id 不能为空。");
        }
    }

    private string BuildJwt(
        AppUserRecord user,
        Guid hospitalId,
        DateTimeOffset expiresAt,
        IReadOnlyList<string> roles)
    {
        var options = jwtOptionsProvider.Options;
        if (string.IsNullOrEmpty(options.Key))
        {
            throw new ValidationException("JWT 密钥未配置（JwtAuth:Key）。");
        }

        var symmetricKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(options.Key));
        var credentials = new SigningCredentials(
            symmetricKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("sub", user.Id.ToString()),
            new("hospitalId", hospitalId.ToString()),
            new("username", user.Code),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        // RBAC：角色码以 ClaimTypes.Role 声明写入令牌，供 IsInRole 策略判定。
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false,
        };
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: DateTimeOffset.UtcNow.AddSeconds(-options.ClockSkewSeconds).UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return handler.WriteToken(token);
    }

    // ===== PBKDF2 密码哈希 =====

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            HashSize);
        return string.Join(
            '$',
            HashVersion,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static bool VerifyPassword(string storedHash, string password)
    {
        if (string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 3 || !string.Equals(parts[0], HashVersion, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] salt;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
