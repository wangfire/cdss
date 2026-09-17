using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Infrastructure.RabbitMq;
using HospitalAi.Worker.Consumers;
using MassTransit;
using Microsoft.Extensions.Options;

namespace HospitalAi.Worker;

/// <summary>
/// Worker 侧 RabbitMQ 消费注册。消费者依赖 Worker 项目，不能放到 Infrastructure。
/// </summary>
public static class RabbitMqWorkerRegistration
{
    public static IServiceCollection AddCodingTaskConsumer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(
            configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddMassTransit(x =>
        {
            x.AddConsumer<CodingTaskCreatedConsumer>();
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
                cfg.ReceiveEndpoint(
                    options.QueueName,
                    endpoint =>
                    {
                        // 队列持久化，避免 RabbitMQ 重启后丢失待处理任务。
                        endpoint.Durable = true;
                        endpoint.AutoDelete = false;
                        endpoint.PrefetchCount = 8;
                        endpoint.BindDeadLetterQueue(
                            "coding.task.created.dead",
                            "coding.task.created.dead");
                        endpoint.UseMessageRetry(retry =>
                        {
                            retry.Immediate(1);
                        });
                        endpoint.ConfigureConsumer<CodingTaskCreatedConsumer>(context);
                    });
            });
        });
        return services;
    }

    private static string NormalizeVirtualHost(string virtualHost)
    {
        return string.IsNullOrWhiteSpace(virtualHost) || virtualHost == "/"
            ? string.Empty
            : $"/{virtualHost.TrimStart('/')}";
    }
}
