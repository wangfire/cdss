namespace HospitalAi.Application.Common;

/// <summary>
/// 表示请求资源不存在，或资源不属于当前医院。
/// </summary>
public sealed class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message)
        : base(message)
    {
    }
}
