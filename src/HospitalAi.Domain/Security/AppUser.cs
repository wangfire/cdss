using HospitalAi.Domain.Common;

namespace HospitalAi.Domain.Security;

/// <summary>
/// 应用用户，保存医院内的编码操作员身份，不保存登录密码（认证由医院侧系统完成）。
/// </summary>
public sealed class AppUser : Entity
{
    private AppUser()
    {
        Status = string.Empty;
    }

    private AppUser(Guid id, Guid hospitalId, string code, string displayName, DateTimeOffset now)
        : base(id, now)
    {
        HospitalId = hospitalId;
        Code = code;
        DisplayName = displayName;
        Status = "ACTIVE";
    }

    public Guid HospitalId { get; } = Guid.Empty;

    public string Code { get; } = string.Empty;

    public string DisplayName { get; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public static AppUser Create(Guid hospitalId, string code, string displayName)
    {
        if (hospitalId == Guid.Empty)
        {
            throw new DomainException("医院标识不能为空。");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("用户工号不能为空。");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("用户显示名称不能为空。");
        }

        return new AppUser(Guid.NewGuid(), hospitalId, code.Trim(), displayName.Trim(), DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 停用或启用用户。
    /// </summary>
    public void SetStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new DomainException("用户状态不能为空。");
        }

        if (status is not ("ACTIVE" or "DISABLED"))
        {
            throw new DomainException($"不支持的用户状态：{status}。");
        }

        Status = status;
        Touch(DateTimeOffset.UtcNow);
    }
}
