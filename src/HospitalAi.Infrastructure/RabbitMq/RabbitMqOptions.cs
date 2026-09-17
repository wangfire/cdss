namespace HospitalAi.Infrastructure.RabbitMq;

/// <summary>
/// RabbitMQ 连接和队列配置。密码只从配置提供，不写入代码。
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";

    public ushort Port { get; set; } = 5672;

    public string VirtualHost { get; set; } = "/";

    public string Username { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string QueueName { get; set; } = "coding.task.created";

    public bool Enabled { get; set; }
}
