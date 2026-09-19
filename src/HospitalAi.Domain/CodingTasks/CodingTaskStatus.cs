namespace HospitalAi.Domain.CodingTasks;

/// <summary>
/// 编码任务的生命周期状态。
/// </summary>
public enum CodingTaskStatus
{
    Pending = 0,
    Running = 1,
    Success = 2,
    Failed = 3,
    Retrying = 4,
    Timeout = 5,
    Cancelled = 6,
    HumanRequired = 7,
    PendingReview = 8,
    Accepted = 9,
    Modified = 10,
    Rejected = 11
}
