namespace HospitalAi.Worker.Retry;

/// <summary>
/// 生产环境使用的真实异步等待器。
/// </summary>
public sealed class TaskRetryDelay : IRetryDelay
{
    public Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        return Task.Delay(delay, cancellationToken);
    }
}
