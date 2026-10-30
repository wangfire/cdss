namespace HospitalAi.Api.Security;

/// <summary>
/// 登录限流选项（配置节 Security:RateLimit）。
/// 按“客户端 IP”分区的固定窗口限流，作为登录锁定的外层防刷补充；
/// 医院内网多为 NAT 出口，窗口额度按科室并发登录峰值放宽，账号级暴力破解由登录锁定兜底。
/// </summary>
public sealed class LoginRateLimitOptions
{
    public const string SectionName = "Security:RateLimit";

    /// <summary>限流窗口内允许的登录请求数，默认 60。设为 0 或负数关闭登录限流。</summary>
    public int LoginPermitLimit { get; set; } = 60;

    /// <summary>限流窗口长度（秒），默认 60。</summary>
    public int LoginWindowSeconds { get; set; } = 60;
}
