namespace HospitalAi.Domain.Common;

/// <summary>
/// 表示违反领域不变量或非法状态转换。
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
