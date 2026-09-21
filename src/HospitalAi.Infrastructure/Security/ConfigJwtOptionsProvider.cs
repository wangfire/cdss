using Microsoft.Extensions.Configuration;

namespace HospitalAi.Infrastructure.Security;

/// <summary>
/// 默认实现：绑定 IConfiguration 的 "JwtAuth" 节。
/// </summary>
public sealed class ConfigJwtOptionsProvider : IJwtOptionsProvider
{
    public JwtAuthOptions Options { get; } = new();

    public ConfigJwtOptionsProvider(IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtAuthOptions.SectionName);
        Options.Key = section.GetValue<string>("Key") ?? string.Empty;
        Options.Issuer = section.GetValue<string>("Issuer") ?? "hospitalai";
        Options.Audience = section.GetValue<string>("Audience") ?? "hospitalai-admin";
        Options.ClockSkewSeconds = section.GetValue("ClockSkewSeconds", 30);
        Options.TokenLifetimeMinutes = section.GetValue("TokenLifetimeMinutes", 120);
    }
}
