namespace HospitalAi.Application.Security;

/// <summary>
/// 登录锁定策略选项（配置节 Security:Lockout）。
/// 连续失败达到 MaxFailedAccessAttempts 锁定 LockoutDuration 分钟，成功登录清零计数。
/// </summary>
public sealed class LockoutOptions
{
    public const string SectionName = "Security:Lockout";

    /// <summary>连续失败次数阈值，默认 5。设为 0 或负数可整体关闭登录锁定。</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>锁定时长（分钟），默认 15。</summary>
    public int LockoutDurationMinutes { get; set; } = 15;
}
