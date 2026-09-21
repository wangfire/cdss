using HospitalAi.Domain.CodingTasks;
using HospitalAi.Domain.Common;

namespace HospitalAi.Domain.Tests;

public sealed class CodingTaskTests
{
    [Fact]
    public void Create_新任务初始状态为待处理()
    {
        var task = CreateTask();

        Assert.Equal(CodingTaskStatus.Pending, task.Status);
        Assert.Equal(0, task.RetryCount);
    }

    [Fact]
    public void StartThenSucceed_将任务从待处理推进到成功()
    {
        var task = CreateTask();

        task.Start();
        task.Succeed();

        Assert.Equal(CodingTaskStatus.Success, task.Status);
        Assert.NotNull(task.StartedAt);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public void Retry_运行中任务进入重试状态并增加重试次数()
    {
        var task = CreateTask();
        task.Start();

        task.Retry("TRANSIENT_ERROR");

        Assert.Equal(CodingTaskStatus.Retrying, task.Status);
        Assert.Equal(1, task.RetryCount);
        Assert.Equal("TRANSIENT_ERROR", task.ErrorCode);
    }

    [Fact]
    public void RetryThenStart_任务可以重新进入运行中()
    {
        var task = CreateTask();
        task.Start();
        task.Retry("TRANSIENT_ERROR");

        task.Start();

        Assert.Equal(CodingTaskStatus.Running, task.Status);
        Assert.Equal(1, task.RetryCount);
        Assert.Null(task.ErrorCode);
    }

    [Fact]
    public void Fail_重试中任务进入失败状态并保留错误码()
    {
        var task = CreateTask();
        task.Start();
        task.Retry("TRANSIENT_ERROR");

        task.Fail("PIPELINE_FAILED");

        Assert.Equal(CodingTaskStatus.Failed, task.Status);
        Assert.Equal("PIPELINE_FAILED", task.ErrorCode);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public void Start_成功任务不能重新启动()
    {
        var task = CreateTask();
        task.Start();
        task.Succeed();

        var exception = Assert.Throws<DomainException>(() => task.Start());

        Assert.Contains("不允许执行操作 Start", exception.Message);
    }

    [Fact]
    public void Succeed_待处理任务不能直接成功()
    {
        var task = CreateTask();

        Assert.Throws<DomainException>(() => task.Succeed());
    }

    [Fact]
    public void ToWireValue_审核状态包含下划线()
    {
        Assert.Equal("PENDING_REVIEW", CodingTaskStatus.PendingReview.ToWireValue());
        Assert.Equal("HUMAN_REQUIRED", CodingTaskStatus.HumanRequired.ToWireValue());
    }

    private static CodingTask CreateTask()
    {
        return CodingTask.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "pipeline-v1");
    }
}
