using HospitalAi.Domain.Common;

namespace HospitalAi.Domain.Security;

/// <summary>
/// 用户-角色关联，保证同一用户同一角色只分配一次。
/// </summary>
public sealed class AppUserRole
{
    private AppUserRole()
    {
    }

    private AppUserRole(Guid id, Guid hospitalId, Guid appUserId, Guid appRoleId, DateTimeOffset now)
    {
        Id = id;
        HospitalId = hospitalId;
        AppUserId = appUserId;
        AppRoleId = appRoleId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid HospitalId { get; }

    public Guid AppUserId { get; }

    public Guid AppRoleId { get; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static AppUserRole Assign(Guid hospitalId, Guid appUserId, Guid appRoleId)
    {
        if (hospitalId == Guid.Empty || appUserId == Guid.Empty || appRoleId == Guid.Empty)
        {
            throw new DomainException("用户-角色分配标识不能为空。");
        }

        return new AppUserRole(Guid.NewGuid(), hospitalId, appUserId, appRoleId, DateTimeOffset.UtcNow);
    }
}
