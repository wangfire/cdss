namespace HospitalAi.Worker.Retry;

/// <summary>
/// 编码任务的固定退避策略。
/// </summary>
public static class RetryPolicy
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(180)
    ];

    public static TimeSpan GetDelay(int retryNumber)
    {
        if (retryNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryNumber));
        }

        return Delays[Math.Min(retryNumber, Delays.Length) - 1];
    }
}
