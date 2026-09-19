using HospitalAi.Api;
using HospitalAi.Api.Observability;
using HospitalAi.Api.Security;
using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Security;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Contracts.CodingKnowledge;
using HospitalAi.Contracts.CodingRecommendations;
using HospitalAi.Contracts.Documents;
using HospitalAi.Contracts.Hospitals;
using HospitalAi.Contracts.Patients;
using HospitalAi.Contracts.Security;
using HospitalAi.Contracts.Visits;
using HospitalAi.Infrastructure.CodingKnowledge;
using HospitalAi.Infrastructure.CodingRecommendations;
using HospitalAi.Infrastructure.CodingTasks;
using HospitalAi.Infrastructure.Observability;
using HospitalAi.Infrastructure.ReferenceData;
using HospitalAi.Infrastructure.RabbitMq;
using HospitalAi.Infrastructure.Security;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Infrastructure.Tracing;
using OpenTelemetry.Exporter.Prometheus;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Host.UseSerilog((context, services, loggerConfiguration) =>
    {
        loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console();
    }, writeToProviders: true);
}

var configuredConnectionString = builder.Configuration.GetConnectionString("HospitalAi");
var connectionString = !string.IsNullOrWhiteSpace(configuredConnectionString)
    ? configuredConnectionString
    : Environment.GetEnvironmentVariable("HOSPITAL_AI_CONNECTION_STRING")
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=HospitalAi;Trusted_Connection=True;TrustServerCertificate=True;";

builder.Services.AddDbContext<HospitalAiDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<RequestContext>();
builder.Services.AddScoped<IRequestContext>(
    serviceProvider => serviceProvider.GetRequiredService<RequestContext>());
builder.Services.AddScoped<ICodingTaskService, SqlServerCodingTaskService>();
builder.Services.AddScoped<ICodingKnowledgeService, SqlServerCodingKnowledgeService>();
builder.Services.AddScoped<ICodingRecommendationService, SqlServerCodingRecommendationService>();
builder.Services.AddScoped<IReferenceDataService, SqlServerReferenceDataService>();
builder.Services.AddScoped<ITraceQueryService, SqlServerTraceQueryService>();
builder.Services.AddScoped<ISecurityService, SqlServerSecurityService>();
builder.Services.Configure<DependencyHealthOptions>(
    builder.Configuration.GetSection(DependencyHealthOptions.SectionName));
builder.Services.AddScoped<ReadinessProbe>();
builder.Services.AddHospitalAiAuthorization();
// OpenTelemetry 指标：HTTP/SQL/运行时 + Prometheus 端点（http://localhost:5080/metrics）。
builder.Services.AddHospitalAiApiMetrics("hospitalai.api");
if (builder.Configuration.GetValue<bool>("RabbitMq:Enabled"))
{
    builder.Services.AddRabbitMqPublisher(builder.Configuration);
}

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestContextMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
// OpenTelemetry Prometheus 端点，供监控抓取 HTTP/SQL/运行时指标。
app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.MapGet(
    "/api/v1/health",
    (IRequestContext requestContext) => Results.Ok(new
    {
        status = "ok",
        traceId = requestContext.TraceId
    }));

app.MapGet(
    "/api/v1/health/live",
    (IRequestContext requestContext) => Results.Ok(new
    {
        status = "ok",
        traceId = requestContext.TraceId
    }));

app.MapGet(
    "/api/v1/health/ready",
    async (
        ReadinessProbe readinessProbe,
        CancellationToken cancellationToken) =>
    {
        var result = await readinessProbe.CheckAsync(cancellationToken);
        return result.Status == "healthy"
            ? Results.Ok(result)
            : Results.Json(
                result,
                statusCode: StatusCodes.Status503ServiceUnavailable);
    });

app.MapPost(
    "/api/v1/code-systems/import",
    async (
        ImportCodeSystemRequest request,
        ICodingKnowledgeService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ImportCodeSystemAsync(request, cancellationToken);
        return Results.Ok(response);
    });

app.MapPost(
    "/api/v1/coding-rules/import",
    async (
        ImportCodingRulesRequest request,
        ICodingKnowledgeService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ImportCodingRulesAsync(request, cancellationToken);
        return Results.Ok(response);
    });

app.MapGet(
    "/api/v1/coding-tasks/{taskId:guid}/recommendations",
    async (
        Guid taskId,
        ICodingRecommendationService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.GetRecommendationsAsync(taskId, cancellationToken);
        return response is null
            ? Results.NotFound()
            : Results.Ok(response);
    });

app.MapPost(
    "/api/v1/coding-tasks/{taskId:guid}/review",
    async (
        Guid taskId,
        ReviewCodingTaskRequest request,
        ICodingRecommendationService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ReviewAsync(taskId, request, cancellationToken);
        return Results.Ok(response);
    });

app.MapGet(
    "/api/v1/workbench/tasks",
    async (
        string? status,
        ICodingRecommendationService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ListWorkbenchTasksAsync(status, cancellationToken);
        return Results.Ok(response);
    });

app.MapGet(
    "/workbench",
    () => Results.Content(
        """
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>智能编码审核工作台</title>
          <style>
            body { margin: 0; font-family: "Segoe UI", Arial, sans-serif; background: #f6f8fb; color: #1f2937; }
            header { padding: 16px 24px; background: #ffffff; border-bottom: 1px solid #d9e2ec; display: flex; justify-content: space-between; align-items: center; }
            main { display: grid; grid-template-columns: 280px 1fr 320px; gap: 16px; padding: 16px; }
            section { background: #ffffff; border: 1px solid #d9e2ec; border-radius: 8px; padding: 16px; min-height: 72vh; }
            h1 { font-size: 20px; margin: 0; }
            h2 { font-size: 16px; margin: 0 0 12px; }
            button { padding: 8px 12px; border: 1px solid #2563eb; background: #2563eb; color: #fff; border-radius: 6px; cursor: pointer; }
            .item { border-bottom: 1px solid #edf2f7; padding: 10px 0; cursor: pointer; }
            .muted { color: #64748b; font-size: 13px; }
            .rec { border: 1px solid #dbeafe; background: #eff6ff; padding: 12px; border-radius: 6px; margin-bottom: 10px; }
            .evidence { color: #475569; font-size: 13px; margin-top: 8px; }
          </style>
        </head>
        <body>
          <header>
            <h1>智能编码审核工作台</h1>
            <button onclick="loadTasks()">刷新任务</button>
          </header>
          <main>
            <section><h2>待审核任务</h2><div id="tasks" class="muted">点击刷新任务</div></section>
            <section><h2>AI 推荐与证据</h2><div id="recommendations" class="muted">选择左侧任务后查看推荐</div></section>
            <section><h2>人工审核</h2><div class="muted">首版请通过接口提交审核：POST /api/v1/coding-tasks/{taskId}/review</div></section>
          </main>
          <script>
            const headers = { "X-Hospital-Id": localStorage.getItem("hospitalId") || "" };
            async function loadTasks() {
              const response = await fetch("/api/v1/workbench/tasks", { headers });
              const tasks = await response.json();
              document.getElementById("tasks").innerHTML = tasks.map(t =>
                `<div class="item" onclick="loadRecommendations('${t.taskId}')"><strong>${t.taskId}</strong><div class="muted">${t.status} · 推荐 ${t.recommendationCount}</div></div>`
              ).join("") || "暂无任务";
            }
            async function loadRecommendations(taskId) {
              const response = await fetch(`/api/v1/coding-tasks/${taskId}/recommendations`, { headers });
              const data = await response.json();
              document.getElementById("recommendations").innerHTML = data.recommendations.map(r =>
                `<div class="rec"><strong>${r.code}</strong> ${r.title}<div class="muted">${r.codeSystem} · ${r.recommendationType} · 置信度 ${r.confidenceScore}</div>${r.evidences.map(e => `<div class="evidence">证据：${e.sourceText}</div>`).join("")}</div>`
              ).join("") || "暂无推荐";
            }
          </script>
        </body>
        </html>
        """,
        "text/html; charset=utf-8"));

app.MapPost(
    "/api/v1/hospitals",
    async (
        CreateHospitalRequest request,
        IReferenceDataService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateHospitalAsync(request, cancellationToken);
        return Results.Created($"/api/v1/hospitals/{response.Id}", response);
    });

app.MapPost(
    "/api/v1/patients",
    async (
        CreatePatientRequest request,
        IReferenceDataService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreatePatientAsync(request, cancellationToken);
        return Results.Created($"/api/v1/patients/{response.Id}", response);
    });

app.MapPost(
    "/api/v1/visits",
    async (
        CreateVisitRequest request,
        IReferenceDataService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateVisitAsync(request, cancellationToken);
        return Results.Created($"/api/v1/visits/{response.Id}", response);
    });

app.MapPost(
    "/api/v1/documents",
    async (
        CreateDocumentRequest request,
        IReferenceDataService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateDocumentAsync(request, cancellationToken);
        return Results.Created($"/api/v1/documents/{response.Id}", response);
    });

app.MapPost(
    "/api/v1/coding-tasks",
    async (
        CreateCodingTaskRequest request,
        ICodingTaskService service,
        IRequestContext requestContext,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateAsync(
            request,
            requestContext.IdempotencyKey ?? string.Empty,
            cancellationToken);
        return Results.Created($"/api/v1/coding-tasks/{response.Id}", response);
    });

app.MapGet(
    "/api/v1/coding-tasks/{taskId:guid}",
    async (
        Guid taskId,
        ICodingTaskService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.GetAsync(taskId, cancellationToken);
        return response is null
            ? Results.NotFound()
            : Results.Ok(response);
    });

app.MapGet(
    "/api/v1/traces/{traceId}",
    async (
        string traceId,
        ITraceQueryService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.GetAsync(traceId, cancellationToken);
        return response is null
            ? Results.NotFound()
            : Results.Ok(response);
    });

app.MapPost(
    "/api/v1/users",
    async (
        CreateUserRequest request,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateUserAsync(request, cancellationToken);
        return Results.Created($"/api/v1/users/{response.Id}", response);
    });

app.MapPost(
    "/api/v1/roles",
    async (
        CreateRoleRequest request,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateRoleAsync(request, cancellationToken);
        return Results.Created($"/api/v1/roles/{response.Id}", response);
    });

app.MapPost(
    "/api/v1/users/{userId:guid}/roles/{roleId:guid}",
    async (
        Guid userId,
        Guid roleId,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        await service.AssignRoleAsync(userId, roleId, cancellationToken);
        return Results.NoContent();
    });

app.MapGet(
    "/api/v1/users/{userId:guid}/roles",
    async (
        Guid userId,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var roles = await service.GetRolesByUserAsync(userId, cancellationToken);
        return Results.Ok(roles);
    });

app.MapPatch(
    "/api/v1/users/{userId:guid}/status",
    async (
        Guid userId,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        await service.SetUserStatusAsync(userId, "DISABLED", cancellationToken);
        return Results.NoContent();
    });

app.Run();

/// <summary>
/// 供 WebApplicationFactory 在 API 集成测试中定位顶级语句生成的宿主类型。
/// </summary>
public partial class Program
{
}
