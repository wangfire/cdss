using HospitalAi.Domain.Common;

namespace HospitalAi.Application.Common;

/// <summary>
/// 登录锁定异常：账号因连续登录失败被临时锁定。
/// 映射 HTTP 423 Locked，并携带剩余锁定分钟数供前端展示。
/// </summary>
public sealed class AccountLockedException : Exception
{
    /// <summary>剩余锁定分钟数（向上取整，至少 1）。</summary>
    public int RemainingLockoutMinutes { get; }

    public AccountLockedException(int remainingLockoutMinutes)
        : base($"登录失败次数过多，账号已临时锁定，请约 {Math.Max(remainingLockoutMinutes, 1)} 分钟后重试或联系管理员解锁。")
    {
        RemainingLockoutMinutes = Math.Max(remainingLockoutMinutes, 1);
    }
}
