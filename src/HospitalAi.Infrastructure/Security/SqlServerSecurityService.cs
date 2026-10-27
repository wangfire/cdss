using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Application.Security;
using HospitalAi.Contracts.Security;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.Security;

/// <summary>
/// 基于 SQL Server 的 RBAC 用户/角色管理实现，所有操作均按医院隔离。
/// </summary>
public sealed class SqlServerSecurityService(
    HospitalAiDbContext dbContext,
    IRequestContext requestContext) : ISecurityService
{
    private const string AdminRoleCode = "ADMIN";

    public async Task<AppUserResponse> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var now = DateTimeOffset.UtcNow;

        var existing = await dbContext.AppUsers
            .SingleOrDefaultAsync(
                item => item.HospitalId == requestContext.HospitalId
                    && item.Code == request.Code.Trim(),
                cancellationToken);

        if (existing is not null)
        {
            return ToResponse(existing);
        }

        var user = new AppUserRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = requestContext.HospitalId,
            Code = request.Code.Trim(),
            DisplayName = request.DisplayName.Trim(),
            Status = "ACTIVE",
            // 创建用户时默认设置初始密码为 Code + "123"，生产环境接入 SSO/目录后应停用此默认值。
            PasswordHash = SqlServerAuthService.HashPassword($"{request.Code.Trim()}123"),
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(user);
    }

    public async Task<IReadOnlyList<AppUserSummaryResponse>> ListUsersAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();

        var users = await dbContext.AppUsers
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId)
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken);

        // 角色表按医院隔离，链接表量级很小，一次取回后在内存里按用户归组，避免 N+1。
        var roleLinks = await dbContext.AppUserRoles
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId)
            .Join(
                dbContext.AppRoles.AsNoTracking(),
                link => link.AppRoleId,
                role => role.Id,
                (link, role) => new { link.AppUserId, role.Code })
            .ToListAsync(cancellationToken);

        return users
            .Select(item => new AppUserSummaryResponse(
                item.Id,
                item.Code,
                item.DisplayName,
                item.Status,
                item.CreatedAt,
                item.LastLoginAt,
                roleLinks
                    .Where(link => link.AppUserId == item.Id)
                    .Select(link => link.Code)
                    .Distinct()
                    .OrderBy(code => code, StringComparer.Ordinal)
                    .ToList()))
            .ToList();
    }

    public async Task<AppRoleResponse> CreateRoleAsync(
        CreateRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var now = DateTimeOffset.UtcNow;

        var existing = await dbContext.AppRoles
            .SingleOrDefaultAsync(
                item => item.HospitalId == requestContext.HospitalId
                    && item.Code == request.Code.Trim(),
                cancellationToken);

        if (existing is not null)
        {
            return ToResponse(existing, await LoadRolePermissionsAsync(existing, cancellationToken));
        }

        var role = new AppRoleRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = requestContext.HospitalId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.AppRoles.Add(role);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(role, PermissionsFor(role, []));
    }

    public async Task<IReadOnlyList<AppRoleResponse>> ListRolesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();

        var roles = await dbContext.AppRoles
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId)
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken);

        var permissionRows = await dbContext.AppRolePermissions
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId)
            .Select(item => new { item.AppRoleId, item.PermissionCode })
            .ToListAsync(cancellationToken);
        var permissionsByRole = permissionRows
            .GroupBy(item => item.AppRoleId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)AppPermissions.Order(
                    group.Select(item => item.PermissionCode)));

        return roles
            .Select(role => ToResponse(
                role,
                PermissionsFor(role, permissionsByRole.GetValueOrDefault(role.Id) ?? [])))
            .ToList();
    }

    public async Task AssignRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();

        var user = await dbContext.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == userId
                    && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (user is null)
        {
            throw new ResourceNotFoundException("应用用户不存在。");
        }

        var role = await dbContext.AppRoles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == roleId
                    && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (role is null)
        {
            throw new ResourceNotFoundException("应用角色不存在。");
        }

        var alreadyAssigned = await dbContext.AppUserRoles
            .AnyAsync(
                item => item.HospitalId == requestContext.HospitalId
                    && item.AppUserId == userId
                    && item.AppRoleId == roleId,
                cancellationToken);
        if (alreadyAssigned)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        dbContext.AppUserRoles.Add(new AppUserRoleRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = requestContext.HospitalId,
            AppUserId = userId,
            AppRoleId = roleId,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();

        var link = await dbContext.AppUserRoles
            .SingleOrDefaultAsync(
                item => item.HospitalId == requestContext.HospitalId
                    && item.AppUserId == userId
                    && item.AppRoleId == roleId,
                cancellationToken);
        if (link is null)
        {
            return;
        }

        var role = await dbContext.AppRoles
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == roleId && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (IsAdminRole(role.Code) && await IsLastActiveAdminAsync(userId, cancellationToken))
        {
            throw new ValidationException("不能移除最后一名启用状态管理员的 ADMIN 角色。");
        }

        dbContext.AppUserRoles.Remove(link);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AppRoleResponse>> GetRolesByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();

        var user = await dbContext.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == userId
                    && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (user is null)
        {
            throw new ResourceNotFoundException("应用用户不存在。");
        }

        var roleIds = await dbContext.AppUserRoles
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.AppUserId == userId)
            .Select(item => item.AppRoleId)
            .ToListAsync(cancellationToken);

        var roleRecords = await dbContext.AppRoles
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && roleIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        var permissionRows = await dbContext.AppRolePermissions
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && roleIds.Contains(item.AppRoleId))
            .Select(item => new { item.AppRoleId, item.PermissionCode })
            .ToListAsync(cancellationToken);
        var permissionsByRole = permissionRows
            .GroupBy(item => item.AppRoleId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)AppPermissions.Order(
                    group.Select(item => item.PermissionCode)));

        return roleRecords
            .Select(role => ToResponse(
                role,
                PermissionsFor(role, permissionsByRole.GetValueOrDefault(role.Id) ?? [])))
            .ToList();
    }

    public async Task SetUserStatusAsync(
        Guid userId,
        string status,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ValidationException("用户状态不能为空。");
        }

        if (status is not ("ACTIVE" or "DISABLED"))
        {
            throw new ValidationException($"不支持的用户状态：{status}。");
        }

        var user = await dbContext.AppUsers
            .SingleOrDefaultAsync(
                item => item.Id == userId
                    && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (user is null)
        {
            throw new ResourceNotFoundException("应用用户不存在。");
        }

        if (status == "DISABLED"
            && string.Equals(user.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)
            && await IsLastActiveAdminAsync(userId, cancellationToken))
        {
            throw new ValidationException("不能停用最后一名启用状态的管理员。");
        }

        user.Status = status;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetRolePermissionsAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var role = await FindRoleAsync(roleId, cancellationToken);
        return await LoadRolePermissionsAsync(role, cancellationToken);
    }

    public async Task SetRolePermissionsAsync(
        Guid roleId,
        IReadOnlyList<string> permissionCodes,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var role = await FindRoleAsync(roleId, cancellationToken);
        if (IsAdminRole(role.Code))
        {
            throw new ValidationException("ADMIN 角色固定拥有全部权限，不能修改。");
        }

        if (permissionCodes is null)
        {
            throw new ValidationException("权限列表不能为空。");
        }

        var codes = permissionCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var unknown = codes.FirstOrDefault(code => !AppPermissions.IsDefined(code));
        if (unknown is not null)
        {
            throw new ValidationException($"不支持的权限码：{unknown}。");
        }

        var existing = await dbContext.AppRolePermissions
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.AppRoleId == roleId)
            .ToListAsync(cancellationToken);
        var existingCodes = existing
            .Select(item => item.PermissionCode)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var row in existing.Where(item => !codes.Contains(item.PermissionCode)))
        {
            dbContext.AppRolePermissions.Remove(row);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var code in codes.Where(code => !existingCodes.Contains(code)))
        {
            dbContext.AppRolePermissions.Add(new AppRolePermissionRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = requestContext.HospitalId,
                AppRoleId = roleId,
                PermissionCode = code,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<AppRoleRecord> FindRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var role = await dbContext.AppRoles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == roleId
                    && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (role is null)
        {
            throw new ResourceNotFoundException("应用角色不存在。");
        }

        return role;
    }

    private async Task<IReadOnlyList<string>> LoadRolePermissionsAsync(
        AppRoleRecord role,
        CancellationToken cancellationToken)
    {
        if (IsAdminRole(role.Code))
        {
            return AppPermissions.All;
        }

        var codes = await dbContext.AppRolePermissions
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.AppRoleId == role.Id)
            .Select(item => item.PermissionCode)
            .ToListAsync(cancellationToken);
        return AppPermissions.Order(codes);
    }

    private static IReadOnlyList<string> PermissionsFor(
        AppRoleRecord role,
        IEnumerable<string> storedPermissions)
    {
        return IsAdminRole(role.Code)
            ? AppPermissions.All
            : AppPermissions.Order(storedPermissions);
    }

    private static bool IsAdminRole(string code)
    {
        return string.Equals(code, AdminRoleCode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断目标用户是否为当前医院最后一名“启用且持有 ADMIN 角色”的用户。
    /// 停用或移除其 ADMIN 角色会让管理面彻底失联，必须拒绝。
    /// </summary>
    private async Task<bool> IsLastActiveAdminAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var hospitalId = requestContext.HospitalId;
        var user = await dbContext.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == userId && item.HospitalId == hospitalId,
                cancellationToken);
        if (user is null
            || !string.Equals(user.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var adminRoleIds = await dbContext.AppRoles
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId && item.Code == AdminRoleCode)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        if (adminRoleIds.Count == 0)
        {
            return false;
        }

        var userIsAdmin = await dbContext.AppUserRoles
            .AsNoTracking()
            .AnyAsync(
                item => item.HospitalId == hospitalId
                    && item.AppUserId == userId
                    && adminRoleIds.Contains(item.AppRoleId),
                cancellationToken);
        if (!userIsAdmin)
        {
            return false;
        }

        var hasOtherActiveAdmin = await dbContext.AppUserRoles
            .AsNoTracking()
            .Where(item => item.HospitalId == hospitalId
                && item.AppUserId != userId
                && adminRoleIds.Contains(item.AppRoleId))
            .Join(
                dbContext.AppUsers
                    .AsNoTracking()
                    .Where(item => item.HospitalId == hospitalId && item.Status == "ACTIVE"),
                link => link.AppUserId,
                other => other.Id,
                (link, other) => link.Id)
            .AnyAsync(cancellationToken);
        return !hasOtherActiveAdmin;
    }

    private void EnsureHospital()
    {
        if (requestContext.HospitalId == Guid.Empty)
        {
            throw new ValidationException("X-Hospital-Id 不能为空。");
        }
    }

    private static AppUserResponse ToResponse(AppUserRecord user)
    {
        return new AppUserResponse(
            user.Id,
            user.HospitalId,
            user.Code,
            user.DisplayName,
            user.Status,
            user.CreatedAt);
    }

    private static AppRoleResponse ToResponse(
        AppRoleRecord role,
        IReadOnlyList<string> permissions)
    {
        return new AppRoleResponse(
            role.Id,
            role.HospitalId,
            role.Code,
            role.Name,
            role.Description,
            role.CreatedAt,
            permissions);
    }
}
