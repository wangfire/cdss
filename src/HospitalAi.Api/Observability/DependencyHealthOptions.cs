namespace HospitalAi.Api.Observability;

/// <summary>
/// 就绪检查依赖配置。Redis 和 RabbitMQ 先用 TCP 探测，避免引入重型客户端。
/// </summary>
public sealed class DependencyHealthOptions
{
    public const string SectionName = "HealthChecks";

    public TcpDependencyOptions Redis { get; set; } = new();

    public TcpDependencyOptions RabbitMq { get; set; } = new();
}

/// <summary>
/// TCP 依赖的主机和端口配置。
/// </summary>
public sealed class TcpDependencyOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; }

    public bool Enabled { get; set; } = true;
}
