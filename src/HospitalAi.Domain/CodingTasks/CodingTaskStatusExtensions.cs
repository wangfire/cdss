namespace HospitalAi.Domain.CodingTasks;

/// <summary>
/// 编码任务状态的外部协议值映射。
/// </summary>
public static class CodingTaskStatusExtensions
{
    public static string ToWireValue(this CodingTaskStatus status)
    {
        return status switch
        {
            CodingTaskStatus.HumanRequired => "HUMAN_REQUIRED",
            CodingTaskStatus.PendingReview => "PENDING_REVIEW",
            CodingTaskStatus.Accepted => "ACCEPTED",
            CodingTaskStatus.Modified => "MODIFIED",
            CodingTaskStatus.Rejected => "REJECTED",
            _ => status.ToString().ToUpperInvariant()
        };
    }
}
