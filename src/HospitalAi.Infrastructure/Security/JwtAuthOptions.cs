namespace HospitalAi.Infrastructure.Security;

/// <summary>
/// JWT 配置，由 Api 层从 "JwtAuth" 配置节绑定并注入。
/// </summary>
public sealed class JwtAuthOptions
{
    public const string SectionName = "JwtAuth";

    public string? Key { get; set; }

    public string Issuer { get; set; } = "hospitalai";

    public string Audience { get; set; } = "hospitalai-admin";

    public int ClockSkewSeconds { get; set; } = 30;

    public int TokenLifetimeMinutes { get; set; } = 120;
}
