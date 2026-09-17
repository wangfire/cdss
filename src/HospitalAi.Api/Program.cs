using HospitalAi.Api;
using HospitalAi.Api.Observability;
using HospitalAi.Api.Security;
using HospitalAi.Application.Abstractions;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Contracts.Documents;
using HospitalAi.Contracts.Hospitals;
using HospitalAi.Contracts.Patients;
using HospitalAi.Contracts.Visits;
using HospitalAi.Infrastructure.CodingTasks;
using HospitalAi.Infrastructure.ReferenceData;
using HospitalAi.Infrastructure.RabbitMq;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Infrastructure.Tracing;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console();
}, writeToProviders: true);

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
builder.Services.AddScoped<IReferenceDataService, SqlServerReferenceDataService>();
builder.Services.AddScoped<ITraceQueryService, SqlServerTraceQueryService>();
builder.Services.Configure<DependencyHealthOptions>(
    builder.Configuration.GetSection(DependencyHealthOptions.SectionName));
builder.Services.AddScoped<ReadinessProbe>();
builder.Services.AddHospitalAiAuthorization();
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

app.Run();

/// <summary>
/// 供 WebApplicationFactory 在 API 集成测试中定位顶级语句生成的宿主类型。
/// </summary>
public partial class Program
{
}
