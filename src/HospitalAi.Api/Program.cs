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
using HospitalAi.Worker;
using OpenTelemetry.Exporter.Prometheus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
builder.Services.AddSingleton(
    _ => CodingTaskPipelineOptions.FromConfiguration(builder.Configuration));
// 灰度开关：新建任务缺省使用的 PipelineVersion。
builder.Services.AddScoped<ICodingTaskService, SqlServerCodingTaskService>();
builder.Services.AddScoped<ICodingKnowledgeService, SqlServerCodingKnowledgeService>();
builder.Services.AddScoped<ICodingRecommendationService, SqlServerCodingRecommendationService>();
builder.Services.AddScoped<IV22CodingService, SqlServerV22CodingService>();
builder.Services.AddScoped<IReferenceDataService, SqlServerReferenceDataService>();
builder.Services.AddScoped<ITraceQueryService, SqlServerTraceQueryService>();
builder.Services.AddScoped<ISecurityService, SqlServerSecurityService>();
builder.Services.AddScoped<IAuthService, SqlServerAuthService>();
builder.Services.AddSingleton<IJwtOptionsProvider, ConfigJwtOptionsProvider>();
// 认证主体由 RequestContextMiddleware 构建（Bearer JWT 或 X-User-Id 占位头）；
// 这里注册直通认证方案，使 RequireAuthorization 策略与 401 Challenge 可用。
builder.Services.AddAuthentication(RequestContextAuthHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, RequestContextAuthHandler>(
        RequestContextAuthHandler.SchemeName, _ => { });
// V2.2-Lite 流水线：Dispatcher 按任务 PipelineVersion 选择 Runner。
// 同时注册模型网关与编码知识检索（Elasticsearch 未启用时自动降级为 Exact 快路径）。
builder.Services.AddCodingTaskPipeline(builder.Configuration);
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

// 前端管理应用跨域（开发环境前端运行在 3001+，生产可走反代同源则无需 CORS）。
builder.Services.AddCors(options =>
    options.AddPolicy("admin", policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    return origin.StartsWith("http://localhost:")
                        || origin.StartsWith("http://127.0.0.1:");
                }
                return origin == "http://localhost:3001"
                    || origin == "http://127.0.0.1:3001";
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    }));

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseCors("admin");
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestContextMiddleware>();
// 认证主体由 RequestContextMiddleware 构建（Bearer JWT 或 X-User-Id 占位头）；
// 这里注册直通认证方案，使 RequireAuthorization 策略与 401 Challenge 可用。
// 显式声明 UseAuthentication/UseAuthorization 的位置（均在 RequestContext 之后），
// 覆盖 WebApplication 默认的“先于用户中间件”自动注入顺序。
app.UseAuthentication();
app.UseAuthorization();
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

// ===== 认证端点（前端 authApi 对齐 /api/v1/auth/*）=====
// 登录请求体含 hospitalId（前端不注入 X-Hospital-Id 头时也能确定医院范围）。
app.MapPost(
    "/api/v1/auth/login",
    async (
        LoginRequest request,
        IAuthService authService,
        CancellationToken cancellationToken) =>
    {
        var response = await authService.LoginAsync(request, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    });

app.MapGet(
    "/api/v1/auth/me",
    async (
        IAuthService authService,
        CancellationToken cancellationToken) =>
    {
        var response = await authService.GetCurrentUserAsync(cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    });

app.MapPost(
    "/api/v1/auth/logout",
    () =>
    {
        // 第一阶段：JWT 短期令牌 + 客户端清除；无服务端黑名单。
        return Results.Ok(ApiEnvelope.Create(new { ok = true }));
    });

app.MapPost(
    "/api/v1/auth/change-password",
    async (
        ChangePasswordRequest request,
        IAuthService authService,
        CancellationToken cancellationToken) =>
    {
        await authService.ChangePasswordAsync(request, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(new { ok = true }));
    });

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
        return Results.Ok(ApiEnvelope.Create(response));
    });

app.MapPost(
    "/api/v1/coding-rules/import",
    async (
        ImportCodingRulesRequest request,
        ICodingKnowledgeService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ImportCodingRulesAsync(request, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    });

// 查询知识库数据
app.MapGet(
    "/api/v1/knowledge/code-systems",
    async (
        HospitalAiDbContext db,
        CancellationToken cancellationToken) =>
    {
        var systems = await db.CodeSystems
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Code, s.Name, s.Version, s.CreatedAt })
            .ToListAsync(cancellationToken);
        return Results.Ok(ApiEnvelope.Create(systems));
    });

app.MapGet(
    "/api/v1/knowledge/medical-codes",
    async (
        HospitalAiDbContext db,
        int? page,
        int? pageSize,
        string? codeSystem,
        string? keyword,
        CancellationToken cancellationToken) =>
    {
        var query = db.MedicalCodes.AsQueryable();
        if (!string.IsNullOrEmpty(codeSystem))
            query = query.Where(c => c.CodeSystemCode == codeSystem);
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(c => c.Code.Contains(keyword) || c.Title.Contains(keyword) || c.SearchText.Contains(keyword));

        var total = await query.CountAsync(cancellationToken);
        var pageNum = page ?? 1;
        var size = pageSize ?? 20;
        var items = await query
            .OrderBy(c => c.Code)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .Select(c => new { c.Id, c.Code, c.Title, c.CodeSystemCode, c.SearchText, c.IsEnabled })
            .ToListAsync(cancellationToken);

        return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
    });

app.MapGet(
    "/api/v1/knowledge/term-synonyms",
    async (
        HospitalAiDbContext db,
        int? page,
        int? pageSize,
        string? keyword,
        CancellationToken cancellationToken) =>
    {
        var query = db.TermSynonyms.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(s => s.Term.Contains(keyword) || s.NormalizedTerm.Contains(keyword));

        var total = await query.CountAsync(cancellationToken);
        var pageNum = page ?? 1;
        var size = pageSize ?? 20;
        var items = await query
            .OrderBy(s => s.Term)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .Select(s => new { s.Id, s.Term, s.NormalizedTerm, s.EntityType, s.CodeSystemCode, s.Code })
            .ToListAsync(cancellationToken);

        return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
    });

app.MapGet(
    "/api/v1/knowledge/coding-rules",
    async (
        HospitalAiDbContext db,
        int? page,
        int? pageSize,
        string? codeSystem,
        CancellationToken cancellationToken) =>
    {
        var query = db.CodingRules.AsQueryable();
        if (!string.IsNullOrEmpty(codeSystem))
            query = query.Where(r => r.CodeSystemCode == codeSystem);

        var total = await query.CountAsync(cancellationToken);
        var pageNum = page ?? 1;
        var size = pageSize ?? 20;
        var items = await query
            .OrderBy(r => r.RuleCode)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .Select(r => new { r.Id, r.RuleCode, CodeSystem = r.CodeSystemCode, r.CodePattern, r.RuleType, r.Severity, r.Message, r.IsEnabled })
            .ToListAsync(cancellationToken);

        return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
    });

app.MapGet(
    "/api/v1/coding-tasks/{taskId:guid}/recommendations",
    async (
        Guid taskId,
        bool? includeLegacy,
        ICodingRecommendationService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.GetRecommendationsAsync(
            taskId,
            includeLegacy ?? false,
            cancellationToken);
        return response is null
            ? Results.NotFound()
            : Results.Ok(ApiEnvelope.Create(response));
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
        return Results.Ok(ApiEnvelope.Create(response));
    });

app.MapGet(
    "/api/v1/workbench/tasks",
    async (
        string? status,
        ICodingRecommendationService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ListWorkbenchTasksAsync(status, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
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
              const envelope = await response.json();
              const tasks = envelope?.data ?? [];
              document.getElementById("tasks").innerHTML = tasks.map(t =>
                `<div class="item" onclick="loadRecommendations('${t.taskId}')"><strong>${t.taskId}</strong><div class="muted">${t.status} · 推荐 ${t.recommendationCount}</div></div>`
              ).join("") || "暂无任务";
            }
            async function loadRecommendations(taskId) {
              const response = await fetch(`/api/v1/coding-tasks/${taskId}/recommendations`, { headers });
              const envelope = await response.json();
              const data = envelope?.data ?? {};
              document.getElementById("recommendations").innerHTML = (data.recommendations ?? []).map(r =>
                `<div class="rec"><strong>${r.code}</strong> ${r.title}<div class="muted">${r.codeSystem} · ${r.recommendationType} · 置信度 ${r.confidenceScore}</div>${(r.evidences ?? []).map(e => `<div class="evidence">证据：${e.sourceText}</div>`).join("")}</div>`
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
        return Results.Created($"/api/v1/hospitals/{response.Id}", ApiEnvelope.Create(response));
    });

app.MapPost(
    "/api/v1/patients",
    async (
        CreatePatientRequest request,
        IReferenceDataService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreatePatientAsync(request, cancellationToken);
        return Results.Created($"/api/v1/patients/{response.Id}", ApiEnvelope.Create(response));
    });

app.MapPost(
    "/api/v1/visits",
    async (
        CreateVisitRequest request,
        IReferenceDataService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateVisitAsync(request, cancellationToken);
        return Results.Created($"/api/v1/visits/{response.Id}", ApiEnvelope.Create(response));
    });

app.MapPost(
    "/api/v1/documents",
    async (
        CreateDocumentRequest request,
        IReferenceDataService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateDocumentAsync(request, cancellationToken);
        return Results.Created($"/api/v1/documents/{response.Id}", ApiEnvelope.Create(response));
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
        return Results.Created($"/api/v1/coding-tasks/{response.Id}", ApiEnvelope.Create(response));
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
            : Results.Ok(ApiEnvelope.Create(response));
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
            ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "TRACE_NOT_FOUND" }))
            : Results.Ok(ApiEnvelope.Create(response));
    });

app.MapGet(
    "/api/v1/coding-tasks/{taskId:guid}/traces",
    async (
        Guid taskId,
        ITraceQueryService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ListRunsAsync(taskId, 20, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);

app.MapPost(
    "/api/v1/users",
    async (
        CreateUserRequest request,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateUserAsync(request, cancellationToken);
        return Results.Created($"/api/v1/users/{response.Id}", ApiEnvelope.Create(response));
    });

app.MapPost(
    "/api/v1/roles",
    async (
        CreateRoleRequest request,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateRoleAsync(request, cancellationToken);
        return Results.Created($"/api/v1/roles/{response.Id}", ApiEnvelope.Create(response));
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
        return Results.Ok(ApiEnvelope.Create(new { ok = true }));
    });

app.MapGet(
    "/api/v1/users/{userId:guid}/roles",
    async (
        Guid userId,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var roles = await service.GetRolesByUserAsync(userId, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(roles));
    });

app.MapPatch(
    "/api/v1/users/{userId:guid}/status",
    async (
        Guid userId,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        await service.SetUserStatusAsync(userId, "DISABLED", cancellationToken);
        return Results.Ok(ApiEnvelope.Create(new { ok = true }));
    });

// ===== V2.2-Lite 新增接口 =====
// 约定：ApiEnvelope 包装；RequestContext 提供医院隔离；
// 默认只返回当前 PipelineVersion 结果，Legacy 必须显式 includeLegacy=true；
// Legacy 结果只读，不接受修改与审核动作。

app.MapGet(
    "/api/v1/visits/{visitId:guid}/readiness",
    async (
        Guid visitId,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.GetVisitReadinessAsync(visitId, cancellationToken);
        return response is null
            ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "VISIT_NOT_FOUND" }))
            : Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);

app.MapGet(
    "/api/v1/visits/{visitId:guid}/clinical-facts",
    async (
        Guid visitId,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.GetClinicalFactsAsync(visitId, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);

app.MapGet(
    "/api/v1/visits/{visitId:guid}/documents",
    async (
        Guid visitId,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.GetVisitDocumentsAsync(visitId, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);

app.MapGet(
    "/api/v1/visits/{visitId:guid}/coding",
    async (
        Guid visitId,
        bool? includeLegacy,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.GetVisitCodingAsync(
            visitId,
            includeLegacy ?? false,
            cancellationToken);
        return response is null
            ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "VISIT_NOT_FOUND" }))
            : Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);

app.MapPost(
    "/api/v1/coding/case-entry",
    async (
        CaseEntryRequest request,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateCaseEntryAsync(request, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

app.MapPost(
    "/api/v1/coding/tasks/{taskId:guid}/diagnoses:batch",
    async (
        Guid taskId,
        DiagnosisInputBatchRequest request,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.SubmitDiagnosisInputsAsync(taskId, request, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

app.MapPost(
    "/api/v1/coding/diagnosis-inputs/{diagnosisInputId:guid}/recommend",
    async (
        Guid diagnosisInputId,
        RecommendRequest? request,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.RecommendAsync(
            diagnosisInputId,
            request ?? new RecommendRequest(DiagnosisInputIds: null),
            cancellationToken);
        return response is null
            ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "RECOMMENDATION_NOT_FOUND" }))
            : Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

app.MapGet(
    "/api/v1/coding/diagnosis-inputs/{diagnosisInputId:guid}/recommendations",
    async (
        Guid diagnosisInputId,
        bool? includeLegacy,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.GetDiagnosisInputRecommendationsAsync(
            diagnosisInputId,
            includeLegacy ?? false,
            cancellationToken);
        return response is null
            ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "DIAGNOSIS_INPUT_NOT_FOUND" }))
            : Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);

app.MapGet(
    "/api/v1/coding/recommendations/{recommendationId:guid}",
    async (
        Guid recommendationId,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.GetRecommendationAsync(recommendationId, cancellationToken);
        return response is null
            ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "RECOMMENDATION_NOT_FOUND" }))
            : Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);

app.MapPost(
    "/api/v1/reviews/{recommendationId:guid}/accept",
    async (
        Guid recommendationId,
        ReviewActionRequest? request,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.AcceptRecommendationAsync(
            recommendationId,
            request ?? new ReviewActionRequest(Comment: null, ReviewerId: null),
            cancellationToken);
        return response is null
            ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "RECOMMENDATION_NOT_FOUND" }))
            : Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

app.MapPost(
    "/api/v1/reviews/{recommendationId:guid}/reject",
    async (
        Guid recommendationId,
        ReviewActionRequest? request,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.RejectRecommendationAsync(
            recommendationId,
            request ?? new ReviewActionRequest(Comment: null, ReviewerId: null),
            cancellationToken);
        return response is null
            ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "RECOMMENDATION_NOT_FOUND" }))
            : Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

app.MapPost(
    "/api/v1/reviews/{recommendationId:guid}/modify",
    async (
        Guid recommendationId,
        ReviewModifyRequest request,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ModifyRecommendationAsync(recommendationId, request, cancellationToken);
        return response is null
            ? Results.NotFound(ApiEnvelope.Create<object>(new { error = "RECOMMENDATION_NOT_FOUND" }))
            : Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

app.MapPost(
    "/api/v1/final-results/submit",
    async (
        FinalResultSubmitRequest request,
        IV22CodingService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.SubmitFinalResultsAsync(request, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

// ===== 知识库索引（Elasticsearch 未启用时返回明确失败，不回退 SQL 全量扫描）=====
app.MapPost(
    "/api/v1/admin/knowledge/index/rebuild",
    async (
        Guid? hospitalId,
        HospitalAi.Infrastructure.Elasticsearch.ICodingKnowledgeIndexer? indexer,
        IRequestContext requestContext,
        IOptions<HospitalAi.Worker.Pipeline.V22PipelineOptions> pipelineOptions,
        CancellationToken cancellationToken) =>
    {
        var targetHospitalId = hospitalId ?? requestContext.HospitalId;
        if (targetHospitalId == Guid.Empty)
        {
            return Results.BadRequest(ApiEnvelope.Create<object>(new { error = "HOSPITAL_NOT_FOUND" }));
        }

        if (indexer is null)
        {
            return Results.Json(
                ApiEnvelope.Create<object>(new { error = "ELASTICSEARCH_DISABLED" }),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var settings = pipelineOptions.Value;
        var result = await indexer.RebuildAsync(
            targetHospitalId,
            settings.KnowledgeVersion,
            settings.DocumentVersion,
            cancellationToken);
        return result.Success
            ? Results.Ok(ApiEnvelope.Create(new { index = result.IndexName, indexed = result.IndexedCount }))
            : Results.Json(
                ApiEnvelope.Create<object>(new { error = "INDEX_REBUILD_FAILED", detail = result.ErrorBody }),
                statusCode: StatusCodes.Status502BadGateway);
    }).RequireAuthorization(RbacPolicies.KnowledgeManager);

app.Run();

/// <summary>
/// 供 WebApplicationFactory 在 API 集成测试中定位顶级语句生成的宿主类型。
/// </summary>
public partial class Program
{
}
