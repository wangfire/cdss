namespace HospitalAi.Domain.CodingTasks;

/// <summary>
/// V2.2 推荐记录生命周期状态。重新运行只做软失效，不物理删除历史证据。
/// </summary>
public enum RecommendationLifecycleStatus
{
    Active = 0,
    Stale = 1,
    LegacyReadOnly = 2,
    Superseded = 3
}

/// <summary>
/// V2.2 推荐生命周期状态的对外协议值映射。
/// </summary>
public static class RecommendationLifecycleStatusExtensions
{
    public static string ToWireValue(this RecommendationLifecycleStatus status)
    {
        return status switch
        {
            RecommendationLifecycleStatus.Stale => "STALE",
            RecommendationLifecycleStatus.LegacyReadOnly => "LEGACY_READ_ONLY",
            RecommendationLifecycleStatus.Superseded => "SUPERSEDED",
            _ => "ACTIVE"
        };
    }
}
