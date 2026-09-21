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
            return ToResponse(existing);
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
        return ToResponse(role);
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

        return roleRecords.Select(ToResponse).ToList();
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

        user.Status = status;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
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

    private static AppRoleResponse ToResponse(AppRoleRecord role)
    {
        return new AppRoleResponse(
            role.Id,
            role.HospitalId,
            role.Code,
            role.Name,
            role.Description,
            role.CreatedAt);
    }
}
