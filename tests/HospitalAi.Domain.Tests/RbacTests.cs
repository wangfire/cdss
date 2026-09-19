using HospitalAi.Domain.Security;
using HospitalAi.Domain.Common;

namespace HospitalAi.Domain.Tests;

public sealed class RbacTests
{
    [Fact]
    public void AppUser_Create_缺少医院标识时抛出领域异常()
    {
        Assert.Throws<DomainException>(() =>
            AppUser.Create(Guid.Empty, "op-001", "张三"));
    }

    [Fact]
    public void AppUser_Create_正常创建时状态为ACTIVE()
    {
        var user = AppUser.Create(Guid.NewGuid(), "op-001", "张三");

        Assert.Equal("ACTIVE", user.Status);
        Assert.Equal("op-001", user.Code);
        Assert.NotEqual(Guid.Empty, user.Id);
    }

    [Fact]
    public void AppUser_SetStatus_支持启用和停用()
    {
        var user = AppUser.Create(Guid.NewGuid(), "op-001", "张三");

        user.SetStatus("DISABLED");

        Assert.Equal("DISABLED", user.Status);
    }

    [Fact]
    public void AppUser_SetStatus_非法状态抛出领域异常()
    {
        var user = AppUser.Create(Guid.NewGuid(), "op-001", "张三");

        Assert.Throws<DomainException>(() => user.SetStatus("UNKNOWN"));
    }

    [Fact]
    public void AppRole_Create_缺少角色名称时抛出领域异常()
    {
        Assert.Throws<DomainException>(() =>
            AppRole.Create(Guid.NewGuid(), "ROLE_CODER", ""));
    }

    [Fact]
    public void AppRole_Create_正常创建时保存代码和名称()
    {
        var role = AppRole.Create(Guid.NewGuid(), "ROLE_CODER", "编码员", "负责病案编码");

        Assert.Equal("ROLE_CODER", role.Code);
        Assert.Equal("编码员", role.Name);
        Assert.Equal("负责病案编码", role.Description);
    }

    [Fact]
    public void AppUserRole_Assign_缺少关联标识时抛出领域异常()
    {
        Assert.Throws<DomainException>(() =>
            AppUserRole.Assign(Guid.NewGuid(), Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    public void AppUserRole_Assign_正常分配时生成关联()
    {
        var hospitalId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var link = AppUserRole.Assign(hospitalId, userId, roleId);

        Assert.Equal(hospitalId, link.HospitalId);
        Assert.Equal(userId, link.AppUserId);
        Assert.Equal(roleId, link.AppRoleId);
        Assert.NotEqual(Guid.Empty, link.Id);
    }
}
