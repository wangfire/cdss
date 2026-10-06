using System.Net;
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

        // disabled 的依赖不计入整体失败，只降级为 degraded。
        var status = dependencies.Values.Any(item => item.Status == "unhealthy")
            ? "unhealthy"
            : dependencies.Values.Any(item => item.Status == "disabled")
                ? "degraded"
                : "healthy";
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
            var addresses = await ResolveAddressesAsync(dependency.Host, cancellationToken);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(2));
            var attempts = addresses
                .Select(async address =>
                {
                    using var client = new TcpClient();
                    try
                    {
                        await client.ConnectAsync(address, dependency.Port, timeoutSource.Token);
                        return (ok: true, error: (string?)null);
                    }
                    catch (Exception exception)
                    {
                        return (ok: false, error: exception.Message);
                    }
                })
                .ToArray();
            var results = await Task.WhenAll(attempts);
            return results.Any(result => result.ok)
                ? DependencyStatus.Healthy()
                : DependencyStatus.Unhealthy(results[0].error ?? "连接超时。");
        }
        catch (Exception exception)
        {
            return DependencyStatus.Unhealthy(exception.Message);
        }
    }

    // "localhost" 可能先解析到 [::1]，而 Redis 仅监听 127.0.0.1；
    // 并发探测全部解析地址，避免 IPv6 重试拖满超时造成误报。
    private static async Task<IPAddress[]> ResolveAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        return addresses.Length > 0 ? addresses : [IPAddress.Loopback];
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
