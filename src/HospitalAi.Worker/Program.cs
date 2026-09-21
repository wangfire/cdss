using HospitalAi.Infrastructure.Observability;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker.Pipeline;
using HospitalAi.Worker.Retry;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Worker;

/// <summary>
/// Worker 进程宿主入口。Api 与 Worker 都会各自生成宿主入口类；
/// 显式命名入口类（而非顶层语句生成的 Program）可避免测试项目同时引用
/// 两个程序集时出现 CS0433 类型冲突。
/// </summary>
public static class WorkerHost
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        var configuredConnectionString = builder.Configuration.GetConnectionString("HospitalAi");
        var connectionString = !string.IsNullOrWhiteSpace(configuredConnectionString)
            ? configuredConnectionString
            : Environment.GetEnvironmentVariable("HOSPITAL_AI_CONNECTION_STRING")
                ?? "Server=(localdb)\\MSSQLLocalDB;Database=HospitalAi;Trusted_Connection=True;TrustServerCertificate=True;";

        builder.Services.AddDbContext<HospitalAiDbContext>(options =>
            options.UseSqlServer(connectionString));
        builder.Services.AddCodingTaskPipeline(builder.Configuration);
        builder.Services.AddScoped<IRetryDelay, TaskRetryDelay>();
        builder.Services.AddCodingTaskConsumer(builder.Configuration);
        // Worker 使用 Worker Service（非 ASP.NET Core）；Prometheus 导出器在 Worker 中仅注册 Meter/Counter 采集，
        // 指标由 MassTransit 内置 Meter（RabbitMQ 消费）+ 自定义 HospitalAi.Worker Meter（流水线）+ SQL/运行时 instrumentation 产生，
        // 可在开发环境通过命令行 `dotnet-counters` 或 OTLP collector 抓取。
        builder.Services.AddHospitalAiWorkerMetrics("hospitalai.worker");

        var host = builder.Build();
        await host.RunAsync();
    }
}
