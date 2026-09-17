namespace HospitalAi.Application.Common;

/// <summary>
/// 表示请求未通过应用层校验。
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationException(string message)
        : base(message)
    {
    }
}
