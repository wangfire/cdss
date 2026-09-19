using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace HospitalAi.Infrastructure.Observability;

/// <summary>
/// 统一的 OpenTelemetry 指标注册，按设计基线 §18 暴露 HTTP、SQL、RabbitMQ 和 Worker 指标。
/// 使用 Prometheus 端点便于开发环境直接抓取，生产可替换为 OTLP 导出器。
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// 注册 API 侧的指标采集：ASP.NET Core HTTP、SQL Client、运行时指标 + Prometheus 导出。
    /// </summary>
    public static IServiceCollection AddHospitalAiApiMetrics(
        this IServiceCollection services,
        string serviceName)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(
                resource => resource.AddService(serviceName, "1.0.0"))
            .WithMetrics(
                metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddSqlClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddPrometheusExporter();
                });

        return services;
    }

    /// <summary>
    /// 注册 Worker 侧的指标采集：SQL Client、运行时指标、MassTransit（RabbitMQ 消费）指标、
    /// 自定义流水线指标 + Prometheus 导出。
    /// </summary>
    public static IServiceCollection AddHospitalAiWorkerMetrics(
        this IServiceCollection services,
        string serviceName)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(
                resource => resource.AddService(serviceName, "1.0.0"))
            .WithMetrics(
                metrics =>
                {
                    metrics
                        .AddSqlClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        // MassTransit 内置 .NET Meter，暴露消费/发布指标。
                        .AddMeter("MassTransit")
                        // 采集 Worker 自定义流水线指标：执行/失败/结果。
                        .AddMeter("HospitalAi.Worker")
                        .AddPrometheusExporter();
                });

        return services;
    }
}
