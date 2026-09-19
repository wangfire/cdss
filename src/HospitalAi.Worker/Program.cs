using HospitalAi.Infrastructure.Observability;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker;
using HospitalAi.Worker.Pipeline;
using HospitalAi.Worker.Retry;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

var configuredConnectionString = builder.Configuration.GetConnectionString("HospitalAi");
var connectionString = !string.IsNullOrWhiteSpace(configuredConnectionString)
    ? configuredConnectionString
    : Environment.GetEnvironmentVariable("HOSPITAL_AI_CONNECTION_STRING")
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=HospitalAi;Trusted_Connection=True;TrustServerCertificate=True;";

builder.Services.AddDbContext<HospitalAiDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<ICodingTaskPipelineRunner, SqlServerCodingRecommendationPipelineRunner>();
builder.Services.AddScoped<IRetryDelay, TaskRetryDelay>();
builder.Services.AddCodingTaskConsumer(builder.Configuration);
// Worker 使用 Worker Service（非 ASP.NET Core）；Prometheus 导出器在 Worker 中仅注册 Meter/Counter 采集，
// 指标由 MassTransit 内置 Meter（RabbitMQ 消费）+ 自定义 HospitalAi.Worker Meter（流水线）+ SQL/运行时 instrumentation 产生，
// 可在开发环境通过命令行 `dotnet-counters` 或 OTLP collector 抓取。
builder.Services.AddHospitalAiWorkerMetrics("hospitalai.worker");

var host = builder.Build();
host.Run();
