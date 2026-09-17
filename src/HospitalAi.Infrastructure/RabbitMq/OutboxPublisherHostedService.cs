using HospitalAi.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HospitalAi.Infrastructure.RabbitMq;

/// <summary>
/// 定时扫描已提交的 Outbox，发布成功后再标记 PublishedAt。
/// </summary>
public sealed class OutboxPublisherHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxPublisherHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var publisher = scope.ServiceProvider.GetRequiredService<OutboxPublisher>();
                await publisher.PublishPendingAsync(cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox 发布失败，将在下一轮重试。");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
