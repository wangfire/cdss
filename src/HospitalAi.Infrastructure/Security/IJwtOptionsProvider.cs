namespace HospitalAi.Infrastructure.Security;

/// <summary>
/// JWT 选项提供器契约，由 Api 层根据配置节 "JwtAuth" 实现并注入。
/// </summary>
public interface IJwtOptionsProvider
{
    JwtAuthOptions Options { get; }
}

