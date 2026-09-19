using HospitalAi.Domain.Common;

namespace HospitalAi.Domain.Security;

/// <summary>
/// 应用角色，保存医院内的最小 RBAC 角色定义，完整权限矩阵待医院接入前确认。
/// </summary>
public sealed class AppRole : Entity
{
    private AppRole()
    {
        Description = string.Empty;
    }

    private AppRole(Guid id, Guid hospitalId, string code, string name, string description, DateTimeOffset now)
        : base(id, now)
    {
        HospitalId = hospitalId;
        Code = code;
        Name = name;
        Description = description;
    }

    public Guid HospitalId { get; } = Guid.Empty;

    public string Code { get; } = string.Empty;

    public string Name { get; } = string.Empty;

    public string Description { get; } = string.Empty;

    public static AppRole Create(Guid hospitalId, string code, string name, string? description = null)
    {
        if (hospitalId == Guid.Empty)
        {
            throw new DomainException("医院标识不能为空。");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("角色代码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("角色名称不能为空。");
        }

        return new AppRole(
            Guid.NewGuid(),
            hospitalId,
            code.Trim(),
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim(),
            DateTimeOffset.UtcNow);
    }
}
