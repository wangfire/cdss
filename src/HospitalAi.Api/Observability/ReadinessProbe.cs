using System.Net.Sockets;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HospitalAi.Api.Observability;

/// <summary>
/// 聚合 API 就绪状态，live 只代表进程存活，ready 才检查外部依赖。
/// </summary>
public sealed class ReadinessProbe(
    HospitalAiDbContext dbContext,
    IOptions<DependencyHealthOptions> options)
{
    public async Task<ReadinessResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var dependencies = new Dictionary<string, DependencyStatus>
        {
            ["sqlServer"] = await CheckSqlServerAsync(cancellationToken),
            ["redis"] = await CheckTcpAsync(options.Value.Redis, cancellationToken),
            ["rabbitMq"] = await CheckTcpAsync(options.Value.RabbitMq, cancellationToken)
        };

        var status = dependencies.Values.All(item => item.Status == "healthy")
            ? "healthy"
            : "unhealthy";
        return new ReadinessResult(status, dependencies);
    }

    private async Task<DependencyStatus> CheckSqlServerAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? DependencyStatus.Healthy()
                : DependencyStatus.Unhealthy("SQL Server 不可连接。");
        }
        catch (Exception exception)
        {
            return DependencyStatus.Unhealthy(exception.Message);
        }
    }

    private static async Task<DependencyStatus> CheckTcpAsync(
        TcpDependencyOptions dependency,
        CancellationToken cancellationToken)
    {
        if (!dependency.Enabled)
        {
            return new DependencyStatus("disabled", null);
        }

        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(
                dependency.Host,
                dependency.Port,
                cancellationToken).AsTask();
            var completed = await Task.WhenAny(
                connectTask,
                Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));

            if (completed != connectTask)
            {
                return DependencyStatus.Unhealthy("连接超时。");
            }

            await connectTask;
            return DependencyStatus.Healthy();
        }
        catch (Exception exception)
        {
            return DependencyStatus.Unhealthy(exception.Message);
        }
    }
}

public sealed record ReadinessResult(
    string Status,
    IReadOnlyDictionary<string, DependencyStatus> Dependencies);

public sealed record DependencyStatus(
    string Status,
    string? Error)
{
    public static DependencyStatus Healthy()
    {
        return new DependencyStatus("healthy", null);
    }

    public static DependencyStatus Unhealthy(string error)
    {
        return new DependencyStatus("unhealthy", error);
    }
}
