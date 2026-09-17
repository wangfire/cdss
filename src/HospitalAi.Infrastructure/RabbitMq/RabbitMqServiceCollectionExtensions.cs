using MassTransit;
using HospitalAi.Contracts.CodingTasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HospitalAi.Infrastructure.Outbox;

namespace HospitalAi.Infrastructure.RabbitMq;

/// <summary>
/// 注册 API 侧发布能力。Worker 的消费者注册保留在 Worker 项目。
/// </summary>
public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqPublisher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddMassTransit(x =>
        {
            x.UsingRabbitMq((context, cfg) =>
            {
                var options = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
                cfg.Host(
                    new Uri(
                        $"rabbitmq://{options.Host}:{options.Port}{NormalizeVirtualHost(options.VirtualHost)}"),
                    host =>
                    {
                        host.Username(options.Username);
                        host.Password(options.Password);
                    });
                cfg.Message<CodingTaskCreatedMessage>(message =>
                {
                    message.SetEntityName("coding.task.created");
                });
            });
        });
        services.AddScoped<IEventBus, RabbitMqEventBus>();
        services.AddScoped<OutboxPublisher>();
        if (bool.TryParse(
                configuration[$"{RabbitMqOptions.SectionName}:Enabled"],
                out var enabled)
            && enabled)
        {
            services.AddHostedService<OutboxPublisherHostedService>();
        }

        return services;
    }

    private static string NormalizeVirtualHost(string virtualHost)
    {
        return string.IsNullOrWhiteSpace(virtualHost) || virtualHost == "/"
            ? string.Empty
            : $"/{virtualHost.TrimStart('/')}";
    }
}
