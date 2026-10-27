namespace HospitalAi.Application.Security;

/// <summary>
/// 权限相关声明。PermissionVersion 缺失表示权限模型上线前签发的旧令牌。
/// </summary>
public static class AppClaimTypes
{
    public const string Permission = "permission";

    public const string PermissionVersion = "permv";

    public const string PermissionVersionValue = "2";
}
