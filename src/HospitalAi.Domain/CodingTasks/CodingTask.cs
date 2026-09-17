using HospitalAi.Domain.Common;

namespace HospitalAi.Domain.CodingTasks;

/// <summary>
/// 表示一次编码处理任务及其状态转换。
/// </summary>
public sealed class CodingTask : Entity
{
    private CodingTask()
    {
    }

    private CodingTask(
        Guid id,
        Guid hospitalId,
        Guid visitId,
        string pipelineVersion,
        DateTimeOffset now)
        : base(id, now)
    {
        HospitalId = hospitalId;
        VisitId = visitId;
        PipelineVersion = pipelineVersion;
        Status = CodingTaskStatus.Pending;
    }

    public Guid HospitalId { get; private set; }

    public Guid VisitId { get; private set; }

    public string PipelineVersion { get; private set; } = string.Empty;

    public CodingTaskStatus Status { get; private set; }

    public int RetryCount { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? ErrorCode { get; private set; }

    public static CodingTask Create(
        Guid hospitalId,
        Guid visitId,
        string pipelineVersion)
    {
        if (string.IsNullOrWhiteSpace(pipelineVersion))
        {
            throw new DomainException("Pipeline 版本不能为空。");
        }

        return new CodingTask(
            Guid.NewGuid(),
            hospitalId,
            visitId,
            pipelineVersion,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 将待处理或等待重试的任务置为运行中。
    /// </summary>
    public void Start()
    {
        EnsureStatus(CodingTaskStatus.Pending, CodingTaskStatus.Retrying, nameof(Start));

        var now = DateTimeOffset.UtcNow;
        Status = CodingTaskStatus.Running;
        StartedAt ??= now;
        CompletedAt = null;
        ErrorCode = null;
        Touch(now);
    }

    /// <summary>
    /// 将运行中的任务标记为成功。
    /// </summary>
    public void Succeed()
    {
        EnsureStatus(CodingTaskStatus.Running, nameof(Succeed));

        var now = DateTimeOffset.UtcNow;
        Status = CodingTaskStatus.Success;
        CompletedAt = now;
        ErrorCode = null;
        Touch(now);
    }

    /// <summary>
    /// 将运行中的任务置为待重试，并记录瞬时错误。
    /// </summary>
    public void Retry(string errorCode)
    {
        EnsureStatus(CodingTaskStatus.Running, nameof(Retry));

        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new DomainException("重试错误码不能为空。");
        }

        var now = DateTimeOffset.UtcNow;
        Status = CodingTaskStatus.Retrying;
        RetryCount++;
        ErrorCode = errorCode;
        Touch(now);
    }

    /// <summary>
    /// 将等待重试的任务标记为最终失败。
    /// </summary>
    public void Fail(string errorCode)
    {
        EnsureStatus(CodingTaskStatus.Retrying, nameof(Fail));

        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new DomainException("失败错误码不能为空。");
        }

        var now = DateTimeOffset.UtcNow;
        Status = CodingTaskStatus.Failed;
        CompletedAt = now;
        ErrorCode = errorCode;
        Touch(now);
    }

    private void EnsureStatus(CodingTaskStatus expectedStatus, string operation)
    {
        EnsureStatus([expectedStatus], operation);
    }

    private void EnsureStatus(
        CodingTaskStatus firstExpectedStatus,
        CodingTaskStatus secondExpectedStatus,
        string operation)
    {
        EnsureStatus([firstExpectedStatus, secondExpectedStatus], operation);
    }

    private void EnsureStatus(
        IReadOnlyCollection<CodingTaskStatus> expectedStatuses,
        string operation)
    {
        if (expectedStatuses.Contains(Status))
        {
            return;
        }

        var expected = string.Join("、", expectedStatuses);
        throw new DomainException(
            $"当前状态 {Status} 不允许执行操作 {operation}，期望状态为 {expected}。");
    }
}
