namespace HospitalAi.Domain.CodingTasks;

/// <summary>
/// 编码业务阶段状态。与 <see cref="CodingTaskStatus"/>（技术任务状态）分离，
/// 避免把 SUCCESS、PENDING_REVIEW、FINALIZED 混进同一个状态体系。
/// </summary>
public enum CodingStage
{
    Imported = 0,
    Processing = 1,
    Ready = 2,
    AiRecommending = 3,
    CoderReview = 4,
    FinalPending = 5,
    Finalized = 6,
    HumanRequired = 7
}
