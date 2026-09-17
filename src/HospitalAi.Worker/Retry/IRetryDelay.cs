namespace HospitalAi.Worker.Retry;

/// <summary>
/// 可替换的重试等待器，测试环境可以注入无等待实现。
/// </summary>
public interface IRetryDelay
{
    Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default);
}
