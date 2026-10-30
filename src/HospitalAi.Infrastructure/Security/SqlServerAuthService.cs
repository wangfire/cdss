using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Application.Security;
using HospitalAi.Contracts.Security;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
/// 登录锁定：连续失败达到 Security:Lockout 阈值即临时锁定，成功登录清零（防暴力破解）。
/// </summary>
public sealed class SqlServerAuthService(
    HospitalAiDbContext dbContext,
    IRequestContext requestContext,
    IJwtOptionsProvider jwtOptionsProvider,
    IOptions<LockoutOptions> lockoutOptions) : IAuthService
{
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string HashVersion = "v1";

    private LockoutOptions Lockout => lockoutOptions.Value;

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

        // 登录锁定（先于密码校验）：被锁定的账号即使密码正确也拒绝，剩余时长对齐锁定截止时间。
        if (user.LockedUntil is { } lockedUntil && lockedUntil > DateTimeOffset.UtcNow)
        {
            throw new AccountLockedException(
                (int)Math.Ceiling((lockedUntil - DateTimeOffset.UtcNow).TotalMinutes));
        }

        if (!VerifyPassword(user.PasswordHash, request.Password))
        {
            var lockedNow = await RecordLoginFailureAsync(user, cancellationToken);
            if (lockedNow)
            {
                // 达到阈值的那次失败直接返回 423，让用户立刻知道账号已锁定。
                throw new AccountLockedException(Lockout.LockoutDurationMinutes);
            }

            throw new ValidationException("用户名或密码错误。");
        }

        if (!string.Equals(user.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("用户已被禁用，请联系管理员。");
        }

        var roles = await GetRoleCodesAsync(user.Id, hospitalId, cancellationToken);
        var permissions = await GetPermissionCodesAsync(hospitalId, roles, cancellationToken);

        // 更新最后登录时间并清零失败计数（尽力而为，失败不影响登录成功）。
        try
        {
            var tracked = await dbContext.AppUsers.SingleAsync(
                item => item.Id == user.Id, cancellationToken);
            tracked.LastLoginAt = DateTimeOffset.UtcNow;
            tracked.UpdatedAt = DateTimeOffset.UtcNow;
            // 成功登录即重置锁定状态：清零失败计数并解除未过期的锁（正常登录路径）。
            tracked.FailedLoginCount = 0;
            tracked.LockedUntil = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // 非关键路径：写失败不阻断登录。
        }

        var now = DateTimeOffset.UtcNow;
        var lifetimeMinutes = Math.Max(jwtOptionsProvider.Options.TokenLifetimeMinutes, 1);
        var expiresAt = now.AddMinutes(lifetimeMinutes);
        var token = BuildJwt(user, hospitalId, expiresAt, roles, permissions);

        return new LoginResponse(
            token,
            expiresAt,
            ToUserDto(user, roles),
            roles,
            permissions);
    }

    /// <summary>
    /// 记录一次登录失败：累加连续失败计数，达到阈值即触发临时锁定。
    /// 返回本次失败是否刚好触发锁定；计数写入失败不改变登录结果（仍按密码错误返回）。
    /// </summary>
    private async Task<bool> RecordLoginFailureAsync(
        AppUserRecord user,
        CancellationToken cancellationToken)
    {
        if (Lockout.MaxFailedAccessAttempts <= 0)
        {
            // 锁定策略关闭：不计数、不锁定。
            return false;
        }

        try
        {
            var tracked = await dbContext.AppUsers.SingleAsync(
                item => item.Id == user.Id, cancellationToken);
            tracked.FailedLoginCount += 1;
            if (tracked.FailedLoginCount >= Lockout.MaxFailedAccessAttempts)
            {
                var nowUtc = DateTimeOffset.UtcNow;
                tracked.LockedUntil = nowUtc.AddMinutes(Lockout.LockoutDurationMinutes);
                tracked.LockoutAt = nowUtc;
                tracked.UpdatedAt = nowUtc;
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }

            tracked.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }
        catch (Exception)
        {
            return false;
        }
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
        var permissions = await GetPermissionCodesAsync(requestContext.HospitalId, roles, cancellationToken);

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
        Guid hospitalId,
        IReadOnlyList<string> roles,
        CancellationToken ct)
    {
        // ADMIN 固定拥有全部权限，避免角色权限配置误伤管理面。
        if (roles.Any(role => string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase)))
        {
            return AppPermissions.All;
        }

        if (roles.Count == 0)
        {
            return [];
        }

        var roleCodes = roles.ToList();
        var codes = await dbContext.AppRolePermissions
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId)
            .Join(
                dbContext.AppRoles
                    .AsNoTracking()
                    .Where(role => role.HospitalId == hospitalId
                        && roleCodes.Contains(role.Code)),
                permission => permission.AppRoleId,
                role => role.Id,
                (permission, role) => permission.PermissionCode)
            .Distinct()
            .ToListAsync(ct);
        return AppPermissions.Order(codes);
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
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions)
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
        // RBAC：角色码保留给管理面展示；鉴权按权限码判定。
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission => new Claim(AppClaimTypes.Permission, permission)));
        // 新令牌即使零权限也带版本声明，避免被当成旧令牌走角色码回退。
        claims.Add(new Claim(
            AppClaimTypes.PermissionVersion,
            AppClaimTypes.PermissionVersionValue));

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
