using HospitalAi.Api;
using HospitalAi.Api.Observability;
using HospitalAi.Api.Security;
using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
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

// 生产环境拒绝占位/过短 JWT 签名密钥：密钥未替换即上线等于允许伪造令牌（WP-B2-5）。
var jwtSigningKey = builder.Configuration["JwtAuth:Key"];
if (builder.Environment.IsProduction()
    && (string.IsNullOrWhiteSpace(jwtSigningKey)
        || jwtSigningKey.StartsWith("REPLACE-WITH", StringComparison.OrdinalIgnoreCase)
        || jwtSigningKey.Length < 32))
{
    throw new InvalidOperationException(
        "生产环境必须配置 ≥32 字节的 JwtAuth:Key（经环境变量或密钥管理注入），禁止使用占位密钥启动。");
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
// 接口文档（WP-B2-2）：Minimal API 元数据 + Swashbuckle UI；中间件仅在非生产环境挂载。
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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
// 接口文档（WP-B2-2）：/swagger 仅非生产环境暴露，生产关闭避免扩大攻击面。
if (!builder.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
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
    }).RequireAuthorization();

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
    }).RequireAuthorization();

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
        HospitalAi.Application.Coding.IcdIndex.IIcdDictionarySource dictionarySource,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ImportCodeSystemAsync(request, cancellationToken);
        dictionarySource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeCodeSystemImport));

app.MapPost(
    "/api/v1/coding-rules/import",
    async (
        ImportCodingRulesRequest request,
        ICodingKnowledgeService service,
        HospitalAi.Application.Coding.Matching.IIcdTermSynonymSource synonymSource,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ImportCodingRulesAsync(request, cancellationToken);
        // 同义词同时写入 icd_term_synonym（索引链路词库），导入后立即生效。
        synonymSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeRulesImport));

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
    }).RequireAuthorization(RbacPolicies.KnowledgeReader);

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
    }).RequireAuthorization(RbacPolicies.KnowledgeReader);

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
    }).RequireAuthorization(RbacPolicies.KnowledgeReader);

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
    }).RequireAuthorization(RbacPolicies.KnowledgeReader);

// ICD 索引库导入（按序号 upsert，全列保留）。
app.MapPost(
    "/api/v1/icd-index/import",
    async (
        ImportIcdIndexRequest request,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.IcdIndex.IIcdIndexSource indexSource,
        CancellationToken cancellationToken) =>
    {
        var now = DateTimeOffset.UtcNow;
        var seqNos = request.Items.Select(item => item.SeqNo).ToArray();
        var existing = await db.IcdIndexes
            .Where(item => seqNos.Contains(item.SeqNo))
            .ToDictionaryAsync(item => item.SeqNo, cancellationToken);

        var imported = 0;
        var updated = 0;
        foreach (var item in request.Items)
        {
            if (existing.TryGetValue(item.SeqNo, out var record))
            {
                record.RawName = item.RawName;
                record.RawNameEn = item.RawNameEn;
                record.IndexName = item.IndexName;
                record.IndexCode = item.IndexCode;
                record.IndexLevel = item.IndexLevel;
                record.ParentSeqNo = item.ParentSeqNo;
                record.FullIndexName = item.FullIndexName;
                record.LeadWordStatus = item.LeadWordStatus;
                record.UpdatedAt = now;
                updated++;
            }
            else
            {
                db.IcdIndexes.Add(new IcdIndexRecord
                {
                    Id = Guid.NewGuid(),
                    SeqNo = item.SeqNo,
                    RawName = item.RawName,
                    RawNameEn = item.RawNameEn,
                    IndexName = item.IndexName,
                    IndexCode = item.IndexCode,
                    IndexLevel = item.IndexLevel,
                    ParentSeqNo = item.ParentSeqNo,
                    FullIndexName = item.FullIndexName,
                    LeadWordStatus = item.LeadWordStatus,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                imported++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        indexSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(
            new ImportIcdIndexResponse(imported, updated)));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeIcdImport));

// 查询 ICD 索引库（leadWordStatus：0=有效，1=无效，缺省返回全部）。
app.MapGet(
    "/api/v1/knowledge/icd-index",
    async (
        HospitalAiDbContext db,
        int? page,
        int? pageSize,
        string? keyword,
        int? leadWordStatus,
        int? indexLevel,
        CancellationToken cancellationToken) =>
    {
        var query = db.IcdIndexes.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(item =>
                item.RawName.Contains(keyword)
                || item.IndexName.Contains(keyword)
                || item.FullIndexName.Contains(keyword)
                || (item.IndexCode != null && item.IndexCode.Contains(keyword))
                || (item.RawNameEn != null && item.RawNameEn.Contains(keyword)));
        }
        if (leadWordStatus is int status)
            query = query.Where(item => item.LeadWordStatus == status);
        if (indexLevel is int level)
            query = query.Where(item => item.IndexLevel == level);

        var total = await query.CountAsync(cancellationToken);
        var pageNum = page ?? 1;
        var size = Math.Min(pageSize ?? 20, 200);
        var items = await query
            .OrderBy(item => item.SeqNo)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .Select(item => new
            {
                item.Id,
                item.SeqNo,
                item.RawName,
                item.RawNameEn,
                item.IndexName,
                item.IndexCode,
                item.IndexLevel,
                item.ParentSeqNo,
                item.FullIndexName,
                item.LeadWordStatus,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
    }).RequireAuthorization(RbacPolicies.KnowledgeReader);

// ICD 索引库节点树：按父序号取直接子节点，并标记 each 节点是否还有下一级。
app.MapGet(
    "/api/v1/knowledge/icd-index/children",
    async (
        HospitalAiDbContext db,
        int? parentSeqNo,
        int? leadWordStatus,
        int? offset,
        int? limit,
        CancellationToken cancellationToken) =>
    {
        var parentId = parentSeqNo ?? 0;
        var take = Math.Min(limit ?? 500, 1000);
        var query = db.IcdIndexes.Where(item => item.ParentSeqNo == parentId);
        if (leadWordStatus is int status)
            query = query.Where(item => item.LeadWordStatus == status);

        var total = await query.CountAsync(cancellationToken);
        var children = await query
            .OrderBy(item => item.SeqNo)
            .Skip(offset ?? 0)
            .Take(take)
            .Select(item => new
            {
                item.Id,
                item.SeqNo,
                item.RawName,
                item.RawNameEn,
                item.IndexName,
                item.IndexCode,
                item.IndexLevel,
                item.ParentSeqNo,
                item.FullIndexName,
                item.LeadWordStatus,
            })
            .ToListAsync(cancellationToken);

        var seqNos = children.Select(item => item.SeqNo).ToArray();
        var parentsWithChildren = await db.IcdIndexes
            .Where(item => seqNos.Contains(item.ParentSeqNo))
            .Select(item => item.ParentSeqNo)
            .Distinct()
            .ToListAsync(cancellationToken);
        var parentSet = parentsWithChildren.ToHashSet();

        var items = children.Select(item => new
        {
            item.Id,
            item.SeqNo,
            item.RawName,
            item.RawNameEn,
            item.IndexName,
            item.IndexCode,
            item.IndexLevel,
            item.ParentSeqNo,
            item.FullIndexName,
            item.LeadWordStatus,
            hasChildren = parentSet.Contains(item.SeqNo),
        });

        return Results.Ok(ApiEnvelope.Create(new { parentSeqNo = parentId, total, items }));
    }).RequireAuthorization(RbacPolicies.KnowledgeReader);

// ICD 索引库单行新增：seqNo 全库唯一，父节点必须已存在；新增后失效快照让下钻用上新树。
app.MapPost(
    "/api/v1/knowledge/icd-index",
    async (
        IndexEntryInput input,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.IcdIndex.IIcdIndexSource indexSource,
        CancellationToken cancellationToken) =>
    {
        var seqNo = input.SeqNo ?? 0;
        var indexName = (input.IndexName ?? string.Empty).Trim();
        var parentSeqNo = input.ParentSeqNo ?? 0;
        var indexLevel = input.IndexLevel ?? 0;
        var leadWordStatus = input.LeadWordStatus ?? 0;
        if (seqNo <= 0)
            throw new ValidationException("序号必须是正整数。");
        if (indexName.Length == 0 || indexName.Length > 200)
            throw new ValidationException("索引名称不能为空且不能超过 200 字。");
        if (parentSeqNo < 0)
            throw new ValidationException("父节点序号不能为负数。");
        if (indexLevel is < 0 or > 9)
            throw new ValidationException("索引级别只能是 0~9。");
        if (leadWordStatus is not (0 or 1))
            throw new ValidationException("主导词状态只能是 0（有效）或 1（无效）。");
        if (parentSeqNo > 0
            && !await db.IcdIndexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
        {
            throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
        }

        if (await db.IcdIndexes.AnyAsync(item => item.SeqNo == seqNo, cancellationToken))
            throw new ValidationException($"序号 {seqNo} 已存在，请换一个序号。");

        var now = DateTimeOffset.UtcNow;
        var rawName = (input.RawName ?? string.Empty).Trim();
        var record = new IcdIndexRecord
        {
            Id = Guid.NewGuid(),
            SeqNo = seqNo,
            RawName = rawName.Length > 0 ? rawName : indexName,
            RawNameEn = TrimToNull(input.RawNameEn),
            IndexName = indexName,
            IndexCode = TrimToNull(input.IndexCode),
            IndexLevel = indexLevel,
            ParentSeqNo = parentSeqNo,
            FullIndexName = TrimToNull(input.FullIndexName) ?? indexName,
            LeadWordStatus = leadWordStatus,
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (record.FullIndexName.Length > 300)
            throw new ValidationException("完整索引名称不能超过 300 字。");
        db.IcdIndexes.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        indexSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeIcdCreate));

// ICD 索引库单行修改：序号不可改（父链按 seqNo 引用），其余字段为空表示沿用原值。
app.MapPut(
    "/api/v1/knowledge/icd-index/{id:guid}",
    async (
        Guid id,
        IndexEntryInput input,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.IcdIndex.IIcdIndexSource indexSource,
        CancellationToken cancellationToken) =>
    {
        var record = await db.IcdIndexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null)
            return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

        var indexName = TrimToNull(input.IndexName) ?? record.IndexName;
        if (indexName.Length > 200)
            throw new ValidationException("索引名称不能超过 200 字。");
        var parentSeqNo = input.ParentSeqNo ?? record.ParentSeqNo;
        if (parentSeqNo < 0)
            throw new ValidationException("父节点序号不能为负数。");
        if (parentSeqNo > 0 && parentSeqNo != record.ParentSeqNo
            && !await db.IcdIndexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
        {
            throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
        }
        if (input.IndexLevel is int level && level is < 0 or > 9)
            throw new ValidationException("索引级别只能是 0~9。");
        if (input.LeadWordStatus is int status && status is not (0 or 1))
            throw new ValidationException("主导词状态只能是 0（有效）或 1（无效）。");

        await EnsureNoIndexParentCycleAsync(
            record.SeqNo,
            parentSeqNo,
            seq => db.IcdIndexes.Where(item => item.SeqNo == seq)
                .Select(item => (int?)item.ParentSeqNo).FirstOrDefaultAsync(cancellationToken),
            cancellationToken);

        record.RawName = TrimToNull(input.RawName) ?? record.RawName;
        record.RawNameEn = input.RawNameEn is null ? record.RawNameEn : TrimToNull(input.RawNameEn);
        record.IndexName = indexName;
        record.IndexCode = input.IndexCode is null ? record.IndexCode : TrimToNull(input.IndexCode);
        record.IndexLevel = input.IndexLevel ?? record.IndexLevel;
        record.ParentSeqNo = parentSeqNo;
        record.FullIndexName = TrimToNull(input.FullIndexName) ?? record.FullIndexName;
        if (record.FullIndexName.Length > 300)
            throw new ValidationException("完整索引名称不能超过 300 字。");
        record.LeadWordStatus = input.LeadWordStatus ?? record.LeadWordStatus;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        indexSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeIcdUpdate));

// ICD 索引库单行删除：有子节点时拒绝（避免父链悬空，先删/迁子节点）。
app.MapDelete(
    "/api/v1/knowledge/icd-index/{id:guid}",
    async (
        Guid id,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.IcdIndex.IIcdIndexSource indexSource,
        CancellationToken cancellationToken) =>
    {
        var record = await db.IcdIndexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null)
            return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

        var childCount = await db.IcdIndexes.CountAsync(
            item => item.ParentSeqNo == record.SeqNo, cancellationToken);
        if (childCount > 0)
            throw new ValidationException($"该节点下还有 {childCount} 个子节点，请先删除或调整子节点。");

        db.IcdIndexes.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
        indexSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { deleted = true }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeIcdDelete));

// CM3（手术操作）索引库导入（按序号 upsert，全列保留）。
app.MapPost(
    "/api/v1/cm3-index/import",
    async (
        ImportCm3IndexRequest request,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.Cm3Index.ICm3IndexSource cm3Source,
        CancellationToken cancellationToken) =>
    {
        var now = DateTimeOffset.UtcNow;
        var seqNos = request.Items.Select(item => item.SeqNo).ToArray();
        var existing = await db.Cm3Indexes
            .Where(item => seqNos.Contains(item.SeqNo))
            .ToDictionaryAsync(item => item.SeqNo, cancellationToken);

        var imported = 0;
        var updated = 0;
        foreach (var item in request.Items)
        {
            if (existing.TryGetValue(item.SeqNo, out var record))
            {
                record.RawName = item.RawName;
                record.RawNameEn = item.RawNameEn;
                record.IndexName = item.IndexName;
                record.IndexCode = item.IndexCode;
                record.IndexLevel = item.IndexLevel;
                record.ParentSeqNo = item.ParentSeqNo;
                record.FullIndexName = item.FullIndexName;
                record.LeadWordStatus = item.LeadWordStatus;
                record.UpdatedAt = now;
                updated++;
            }
            else
            {
                db.Cm3Indexes.Add(new Cm3IndexRecord
                {
                    Id = Guid.NewGuid(),
                    SeqNo = item.SeqNo,
                    RawName = item.RawName,
                    RawNameEn = item.RawNameEn,
                    IndexName = item.IndexName,
                    IndexCode = item.IndexCode,
                    IndexLevel = item.IndexLevel,
                    ParentSeqNo = item.ParentSeqNo,
                    FullIndexName = item.FullIndexName,
                    LeadWordStatus = item.LeadWordStatus,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                imported++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        // 与 ICD 索引导入对称：快照是单例内存缓存，不失效就会一直用导入前的旧树。
        cm3Source.Invalidate();
        return Results.Ok(ApiEnvelope.Create(
            new ImportCm3IndexResponse(imported, updated)));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeCm3Import));

// 查询 CM3 索引库（leadWordStatus：0=有效，1=无效，缺省返回全部）。
app.MapGet(
    "/api/v1/knowledge/cm3-index",
    async (
        HospitalAiDbContext db,
        int? page,
        int? pageSize,
        string? keyword,
        int? leadWordStatus,
        int? indexLevel,
        CancellationToken cancellationToken) =>
    {
        var query = db.Cm3Indexes.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(item =>
                item.RawName.Contains(keyword)
                || item.IndexName.Contains(keyword)
                || item.FullIndexName.Contains(keyword)
                || (item.IndexCode != null && item.IndexCode.Contains(keyword))
                || (item.RawNameEn != null && item.RawNameEn.Contains(keyword)));
        }
        if (leadWordStatus is int status)
            query = query.Where(item => item.LeadWordStatus == status);
        if (indexLevel is int level)
            query = query.Where(item => item.IndexLevel == level);

        var total = await query.CountAsync(cancellationToken);
        var pageNum = page ?? 1;
        var size = Math.Min(pageSize ?? 20, 200);
        var items = await query
            .OrderBy(item => item.SeqNo)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .Select(item => new
            {
                item.Id,
                item.SeqNo,
                item.RawName,
                item.RawNameEn,
                item.IndexName,
                item.IndexCode,
                item.IndexLevel,
                item.ParentSeqNo,
                item.FullIndexName,
                item.LeadWordStatus,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
    }).RequireAuthorization(RbacPolicies.KnowledgeReader);

// CM3 索引库节点树：按父序号取直接子节点，并标记每个节点是否还有下一级。
app.MapGet(
    "/api/v1/knowledge/cm3-index/children",
    async (
        HospitalAiDbContext db,
        int? parentSeqNo,
        int? leadWordStatus,
        int? offset,
        int? limit,
        CancellationToken cancellationToken) =>
    {
        var parentId = parentSeqNo ?? 0;
        var take = Math.Min(limit ?? 500, 1000);
        var query = db.Cm3Indexes.Where(item => item.ParentSeqNo == parentId);
        if (leadWordStatus is int status)
            query = query.Where(item => item.LeadWordStatus == status);

        var total = await query.CountAsync(cancellationToken);
        var children = await query
            .OrderBy(item => item.SeqNo)
            .Skip(offset ?? 0)
            .Take(take)
            .Select(item => new
            {
                item.Id,
                item.SeqNo,
                item.RawName,
                item.RawNameEn,
                item.IndexName,
                item.IndexCode,
                item.IndexLevel,
                item.ParentSeqNo,
                item.FullIndexName,
                item.LeadWordStatus,
            })
            .ToListAsync(cancellationToken);

        var seqNos = children.Select(item => item.SeqNo).ToArray();
        var parentsWithChildren = await db.Cm3Indexes
            .Where(item => seqNos.Contains(item.ParentSeqNo))
            .Select(item => item.ParentSeqNo)
            .Distinct()
            .ToListAsync(cancellationToken);
        var parentSet = parentsWithChildren.ToHashSet();

        var items = children.Select(item => new
        {
            item.Id,
            item.SeqNo,
            item.RawName,
            item.RawNameEn,
            item.IndexName,
            item.IndexCode,
            item.IndexLevel,
            item.ParentSeqNo,
            item.FullIndexName,
            item.LeadWordStatus,
            hasChildren = parentSet.Contains(item.SeqNo),
        });

        return Results.Ok(ApiEnvelope.Create(new { parentSeqNo = parentId, total, items }));
    }).RequireAuthorization(RbacPolicies.KnowledgeReader);

// CM3 索引库单行新增：与 ICD 索引库同一套校验（序号唯一、父节点存在、级别 0~9）。
app.MapPost(
    "/api/v1/knowledge/cm3-index",
    async (
        IndexEntryInput input,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.Cm3Index.ICm3IndexSource cm3Source,
        CancellationToken cancellationToken) =>
    {
        var seqNo = input.SeqNo ?? 0;
        var indexName = (input.IndexName ?? string.Empty).Trim();
        var parentSeqNo = input.ParentSeqNo ?? 0;
        var indexLevel = input.IndexLevel ?? 0;
        var leadWordStatus = input.LeadWordStatus ?? 0;
        if (seqNo <= 0)
            throw new ValidationException("序号必须是正整数。");
        if (indexName.Length == 0 || indexName.Length > 200)
            throw new ValidationException("索引名称不能为空且不能超过 200 字。");
        if (parentSeqNo < 0)
            throw new ValidationException("父节点序号不能为负数。");
        if (indexLevel is < 0 or > 9)
            throw new ValidationException("索引级别只能是 0~9。");
        if (leadWordStatus is not (0 or 1))
            throw new ValidationException("主导词状态只能是 0（有效）或 1（无效）。");
        if (parentSeqNo > 0
            && !await db.Cm3Indexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
        {
            throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
        }

        if (await db.Cm3Indexes.AnyAsync(item => item.SeqNo == seqNo, cancellationToken))
            throw new ValidationException($"序号 {seqNo} 已存在，请换一个序号。");

        var now = DateTimeOffset.UtcNow;
        var rawName = (input.RawName ?? string.Empty).Trim();
        var record = new Cm3IndexRecord
        {
            Id = Guid.NewGuid(),
            SeqNo = seqNo,
            RawName = rawName.Length > 0 ? rawName : indexName,
            RawNameEn = TrimToNull(input.RawNameEn),
            IndexName = indexName,
            IndexCode = TrimToNull(input.IndexCode),
            IndexLevel = indexLevel,
            ParentSeqNo = parentSeqNo,
            FullIndexName = TrimToNull(input.FullIndexName) ?? indexName,
            LeadWordStatus = leadWordStatus,
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (record.FullIndexName.Length > 300)
            throw new ValidationException("完整索引名称不能超过 300 字。");
        db.Cm3Indexes.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        cm3Source.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeCm3Create));

// CM3 索引库单行修改：序号不可改（父链按 seqNo 引用），其余字段为空表示沿用原值。
app.MapPut(
    "/api/v1/knowledge/cm3-index/{id:guid}",
    async (
        Guid id,
        IndexEntryInput input,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.Cm3Index.ICm3IndexSource cm3Source,
        CancellationToken cancellationToken) =>
    {
        var record = await db.Cm3Indexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null)
            return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

        var indexName = TrimToNull(input.IndexName) ?? record.IndexName;
        if (indexName.Length > 200)
            throw new ValidationException("索引名称不能超过 200 字。");
        var parentSeqNo = input.ParentSeqNo ?? record.ParentSeqNo;
        if (parentSeqNo < 0)
            throw new ValidationException("父节点序号不能为负数。");
        if (parentSeqNo > 0 && parentSeqNo != record.ParentSeqNo
            && !await db.Cm3Indexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
        {
            throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
        }
        if (input.IndexLevel is int level && level is < 0 or > 9)
            throw new ValidationException("索引级别只能是 0~9。");
        if (input.LeadWordStatus is int status && status is not (0 or 1))
            throw new ValidationException("主导词状态只能是 0（有效）或 1（无效）。");

        await EnsureNoIndexParentCycleAsync(
            record.SeqNo,
            parentSeqNo,
            seq => db.Cm3Indexes.Where(item => item.SeqNo == seq)
                .Select(item => (int?)item.ParentSeqNo).FirstOrDefaultAsync(cancellationToken),
            cancellationToken);

        record.RawName = TrimToNull(input.RawName) ?? record.RawName;
        record.RawNameEn = input.RawNameEn is null ? record.RawNameEn : TrimToNull(input.RawNameEn);
        record.IndexName = indexName;
        record.IndexCode = input.IndexCode is null ? record.IndexCode : TrimToNull(input.IndexCode);
        record.IndexLevel = input.IndexLevel ?? record.IndexLevel;
        record.ParentSeqNo = parentSeqNo;
        record.FullIndexName = TrimToNull(input.FullIndexName) ?? record.FullIndexName;
        if (record.FullIndexName.Length > 300)
            throw new ValidationException("完整索引名称不能超过 300 字。");
        record.LeadWordStatus = input.LeadWordStatus ?? record.LeadWordStatus;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        cm3Source.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeCm3Update));

// CM3 索引库单行删除：有子节点时拒绝（避免父链悬空，先删/迁子节点）。
app.MapDelete(
    "/api/v1/knowledge/cm3-index/{id:guid}",
    async (
        Guid id,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.Cm3Index.ICm3IndexSource cm3Source,
        CancellationToken cancellationToken) =>
    {
        var record = await db.Cm3Indexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null)
            return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

        var childCount = await db.Cm3Indexes.CountAsync(
            item => item.ParentSeqNo == record.SeqNo, cancellationToken);
        if (childCount > 0)
            throw new ValidationException($"该节点下还有 {childCount} 个子节点，请先删除或调整子节点。");

        db.Cm3Indexes.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
        cm3Source.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { deleted = true }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeCm3Delete));

// 查询肿瘤索引库（"肿瘤，肿瘤性"部位表：每行并排五个动态码，无主导词状态列）。
app.MapGet(
    "/api/v1/knowledge/tumor-index",
    async (
        HospitalAiDbContext db,
        int? page,
        int? pageSize,
        string? keyword,
        int? indexLevel,
        CancellationToken cancellationToken) =>
    {
        var query = db.TumorIndexes.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(item =>
                item.IndexName.Contains(keyword)
                || item.FullIndexName.Contains(keyword)
                || (item.IndexNameEn != null && item.IndexNameEn.Contains(keyword))
                || (item.CodeMalignantPrimary != null && item.CodeMalignantPrimary.Contains(keyword))
                || (item.CodeMalignantSecondary != null && item.CodeMalignantSecondary.Contains(keyword))
                || (item.CodeInSitu != null && item.CodeInSitu.Contains(keyword))
                || (item.CodeBenign != null && item.CodeBenign.Contains(keyword))
                || (item.CodeUncertain != null && item.CodeUncertain.Contains(keyword))
                || (item.JumpTarget != null && item.JumpTarget.Contains(keyword)));
        }
        if (indexLevel is int level)
            query = query.Where(item => item.IndexLevel == level);

        var total = await query.CountAsync(cancellationToken);
        var pageNum = page ?? 1;
        var size = Math.Min(pageSize ?? 20, 200);
        var items = await query
            .OrderBy(item => item.SeqNo)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .Select(item => new
            {
                item.Id,
                item.SeqNo,
                item.IndexName,
                item.IndexNameEn,
                item.IndexLevel,
                item.ParentSeqNo,
                item.FullIndexName,
                item.CodeMalignantPrimary,
                item.CodeMalignantSecondary,
                item.CodeInSitu,
                item.CodeBenign,
                item.CodeUncertain,
                item.JumpKind,
                item.JumpTarget,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
    }).RequireAuthorization(RbacPolicies.KnowledgeReader);

// 肿瘤索引库节点树：按父序号取直接子节点，并标记每个节点是否还有下一级。
app.MapGet(
    "/api/v1/knowledge/tumor-index/children",
    async (
        HospitalAiDbContext db,
        int? parentSeqNo,
        int? offset,
        int? limit,
        CancellationToken cancellationToken) =>
    {
        var parentId = parentSeqNo ?? 0;
        var take = Math.Min(limit ?? 500, 1000);
        var query = db.TumorIndexes.Where(item => item.ParentSeqNo == parentId);

        var total = await query.CountAsync(cancellationToken);
        var children = await query
            .OrderBy(item => item.SeqNo)
            .Skip(offset ?? 0)
            .Take(take)
            .Select(item => new
            {
                item.Id,
                item.SeqNo,
                item.IndexName,
                item.IndexNameEn,
                item.IndexLevel,
                item.ParentSeqNo,
                item.FullIndexName,
                item.CodeMalignantPrimary,
                item.CodeMalignantSecondary,
                item.CodeInSitu,
                item.CodeBenign,
                item.CodeUncertain,
                item.JumpKind,
                item.JumpTarget,
            })
            .ToListAsync(cancellationToken);

        var seqNos = children.Select(item => item.SeqNo).ToArray();
        var parentsWithChildren = await db.TumorIndexes
            .Where(item => seqNos.Contains(item.ParentSeqNo))
            .Select(item => item.ParentSeqNo)
            .Distinct()
            .ToListAsync(cancellationToken);
        var parentSet = parentsWithChildren.ToHashSet();

        var items = children.Select(item => new
        {
            item.Id,
            item.SeqNo,
            item.IndexName,
            item.IndexNameEn,
            item.IndexLevel,
            item.ParentSeqNo,
            item.FullIndexName,
            item.CodeMalignantPrimary,
            item.CodeMalignantSecondary,
            item.CodeInSitu,
            item.CodeBenign,
            item.CodeUncertain,
            item.JumpKind,
            item.JumpTarget,
            hasChildren = parentSet.Contains(item.SeqNo),
        });

        return Results.Ok(ApiEnvelope.Create(new { parentSeqNo = parentId, total, items }));
    }).RequireAuthorization(RbacPolicies.KnowledgeReader);

// 肿瘤索引单行新增：序号唯一、父节点存在、跳转类型为 见/另见 时必须给目标词。
app.MapPost(
    "/api/v1/knowledge/tumor-index",
    async (
        TumorIndexEntryInput input,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.IcdIndex.ITumorIndexSource tumorSource,
        CancellationToken cancellationToken) =>
    {
        var seqNo = input.SeqNo ?? 0;
        var indexName = (input.IndexName ?? string.Empty).Trim();
        var parentSeqNo = input.ParentSeqNo ?? 0;
        var indexLevel = input.IndexLevel ?? 0;
        var jumpKind = input.JumpKind ?? 0;
        var jumpTarget = TrimToNull(input.JumpTarget);
        if (seqNo <= 0)
            throw new ValidationException("序号必须是正整数。");
        if (indexName.Length == 0 || indexName.Length > 200)
            throw new ValidationException("索引名称不能为空且不能超过 200 字。");
        if (parentSeqNo < 0)
            throw new ValidationException("父节点序号不能为负数。");
        if (indexLevel is < 0 or > 9)
            throw new ValidationException("索引级别只能是 0~9。");
        if (jumpKind is not (0 or 1 or 2))
            throw new ValidationException("跳转类型只能是 无跳转/见/另见。");
        if (jumpKind != 0 && jumpTarget is null)
            throw new ValidationException("跳转类型为「见」或「另见」时必须填写跳转目标词。");
        if (parentSeqNo > 0
            && !await db.TumorIndexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
        {
            throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
        }

        if (await db.TumorIndexes.AnyAsync(item => item.SeqNo == seqNo, cancellationToken))
            throw new ValidationException($"序号 {seqNo} 已存在，请换一个序号。");

        var now = DateTimeOffset.UtcNow;
        var record = new TumorIndexRecord
        {
            Id = Guid.NewGuid(),
            SeqNo = seqNo,
            IndexName = indexName,
            IndexNameEn = TrimToNull(input.IndexNameEn),
            IndexLevel = indexLevel,
            ParentSeqNo = parentSeqNo,
            FullIndexName = TrimToNull(input.FullIndexName) ?? indexName,
            CodeMalignantPrimary = TrimToNull(input.CodeMalignantPrimary),
            CodeMalignantSecondary = TrimToNull(input.CodeMalignantSecondary),
            CodeInSitu = TrimToNull(input.CodeInSitu),
            CodeBenign = TrimToNull(input.CodeBenign),
            CodeUncertain = TrimToNull(input.CodeUncertain),
            JumpKind = jumpKind,
            JumpTarget = jumpKind == 0 ? null : jumpTarget,
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (record.FullIndexName.Length > 300)
            throw new ValidationException("完整索引名称不能超过 300 字。");
        db.TumorIndexes.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        tumorSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeTumorCreate));

// 肿瘤索引单行修改：序号不可改（父链按 seqNo 引用），其余字段为空表示沿用原值。
app.MapPut(
    "/api/v1/knowledge/tumor-index/{id:guid}",
    async (
        Guid id,
        TumorIndexEntryInput input,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.IcdIndex.ITumorIndexSource tumorSource,
        CancellationToken cancellationToken) =>
    {
        var record = await db.TumorIndexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null)
            return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

        var indexName = TrimToNull(input.IndexName) ?? record.IndexName;
        if (indexName.Length > 200)
            throw new ValidationException("索引名称不能超过 200 字。");
        var parentSeqNo = input.ParentSeqNo ?? record.ParentSeqNo;
        if (parentSeqNo < 0)
            throw new ValidationException("父节点序号不能为负数。");
        if (parentSeqNo > 0 && parentSeqNo != record.ParentSeqNo
            && !await db.TumorIndexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
        {
            throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
        }
        if (input.IndexLevel is int level && level is < 0 or > 9)
            throw new ValidationException("索引级别只能是 0~9。");
        if (input.JumpKind is int kind && kind is not (0 or 1 or 2))
            throw new ValidationException("跳转类型只能是 无跳转/见/另见。");

        await EnsureNoIndexParentCycleAsync(
            record.SeqNo,
            parentSeqNo,
            seq => db.TumorIndexes.Where(item => item.SeqNo == seq)
                .Select(item => (int?)item.ParentSeqNo).FirstOrDefaultAsync(cancellationToken),
            cancellationToken);

        var jumpKind = input.JumpKind ?? record.JumpKind;
        var jumpTarget = input.JumpTarget is null ? record.JumpTarget : TrimToNull(input.JumpTarget);
        if (jumpKind != 0 && jumpTarget is null)
            throw new ValidationException("跳转类型为「见」或「另见」时必须填写跳转目标词。");

        record.IndexName = indexName;
        record.IndexNameEn = input.IndexNameEn is null ? record.IndexNameEn : TrimToNull(input.IndexNameEn);
        record.IndexLevel = input.IndexLevel ?? record.IndexLevel;
        record.ParentSeqNo = parentSeqNo;
        record.FullIndexName = TrimToNull(input.FullIndexName) ?? record.FullIndexName;
        if (record.FullIndexName.Length > 300)
            throw new ValidationException("完整索引名称不能超过 300 字。");
        record.CodeMalignantPrimary = input.CodeMalignantPrimary is null
            ? record.CodeMalignantPrimary
            : TrimToNull(input.CodeMalignantPrimary);
        record.CodeMalignantSecondary = input.CodeMalignantSecondary is null
            ? record.CodeMalignantSecondary
            : TrimToNull(input.CodeMalignantSecondary);
        record.CodeInSitu = input.CodeInSitu is null ? record.CodeInSitu : TrimToNull(input.CodeInSitu);
        record.CodeBenign = input.CodeBenign is null ? record.CodeBenign : TrimToNull(input.CodeBenign);
        record.CodeUncertain = input.CodeUncertain is null
            ? record.CodeUncertain
            : TrimToNull(input.CodeUncertain);
        record.JumpKind = jumpKind;
        record.JumpTarget = jumpKind == 0 ? null : jumpTarget;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        tumorSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeTumorUpdate));

// 肿瘤索引单行删除：有子节点时拒绝（避免父链悬空，先删/迁子节点）。
app.MapDelete(
    "/api/v1/knowledge/tumor-index/{id:guid}",
    async (
        Guid id,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.IcdIndex.ITumorIndexSource tumorSource,
        CancellationToken cancellationToken) =>
    {
        var record = await db.TumorIndexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null)
            return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

        var childCount = await db.TumorIndexes.CountAsync(
            item => item.ParentSeqNo == record.SeqNo, cancellationToken);
        if (childCount > 0)
            throw new ValidationException($"该节点下还有 {childCount} 个子节点，请先删除或调整子节点。");

        db.TumorIndexes.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
        tumorSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { deleted = true }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeTumorDelete));

app.MapPost(
    "/api/v1/icd-term-synonym/import",
    async (
        ImportTermSynonymRequest request,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.Matching.IIcdTermSynonymSource synonymSource,
        CancellationToken cancellationToken) =>
    {
        var now = DateTimeOffset.UtcNow;
        var pairs = request.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.CanonicalTerm))
            .SelectMany(item => item.Variants
                .Where(variant => !string.IsNullOrWhiteSpace(variant))
                .Select(variant => new { item.TermType, Canonical = item.CanonicalTerm.Trim(), Variant = variant.Trim() }))
            .GroupBy(pair => new { pair.TermType, pair.Variant })
            .Select(group => group.First())
            .ToList();

        var typeKeys = pairs.Select(pair => pair.TermType).Distinct().ToArray();
        var variantKeys = pairs.Select(pair => pair.Variant).ToArray();
        var existing = await db.IcdTermSynonyms
            .Where(item => typeKeys.Contains(item.TermType) && variantKeys.Contains(item.VariantTerm))
            .ToListAsync(cancellationToken);
        var existingMap = existing.ToDictionary(item => (item.TermType, item.VariantTerm));

        var imported = 0;
        var updated = 0;
        foreach (var pair in pairs)
        {
            if (existingMap.TryGetValue((pair.TermType, pair.Variant), out var record))
            {
                if (!record.IsEnabled || record.CanonicalTerm != pair.Canonical)
                {
                    record.CanonicalTerm = pair.Canonical;
                    record.IsEnabled = true;
                    record.UpdatedAt = now;
                    updated++;
                }
            }
            else
            {
                db.IcdTermSynonyms.Add(new IcdTermSynonymRecord
                {
                    Id = Guid.NewGuid(),
                    TermType = pair.TermType,
                    CanonicalTerm = pair.Canonical,
                    VariantTerm = pair.Variant,
                    IsEnabled = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                imported++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        synonymSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new ImportTermSynonymResponse(imported, updated)));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeSynonymImport));

// 术语同义词组查询（按 类型+规范词 聚合展示，停用行一并返回供页面维护）。
app.MapGet(
    "/api/v1/knowledge/icd-term-synonym",
    async (
        string? termType,
        string? keyword,
        HospitalAiDbContext db,
        CancellationToken cancellationToken) =>
    {
        var query = db.IcdTermSynonyms.AsQueryable();
        if (!string.IsNullOrEmpty(termType))
            query = query.Where(item => item.TermType == termType);
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(item =>
                item.CanonicalTerm.Contains(keyword) || item.VariantTerm.Contains(keyword));

        var rows = await query
            .OrderBy(item => item.TermType)
            .ThenBy(item => item.CanonicalTerm)
            .ThenBy(item => item.VariantTerm)
            .ToListAsync(cancellationToken);

        var groups = rows
            .GroupBy(item => new { item.TermType, item.CanonicalTerm })
            .Select(group => new
            {
                termType = group.Key.TermType,
                canonicalTerm = group.Key.CanonicalTerm,
                variants = group
                    .Select(item => new { item.Id, item.VariantTerm, item.IsEnabled })
                    .ToList(),
            })
            .ToList();

        return Results.Ok(ApiEnvelope.Create(new { total = groups.Count, items = groups }));
    }).RequireAuthorization(RbacPolicies.KnowledgeReader);

// 单条同义词新增：在等价类里补一个等同词（类内任意两词在匹配时可互换）。
app.MapPost(
    "/api/v1/knowledge/icd-term-synonym",
    async (
        IcdTermSynonymInput input,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.Matching.IIcdTermSynonymSource synonymSource,
        CancellationToken cancellationToken) =>
    {
        var termType = (input.TermType ?? string.Empty).Trim().ToUpperInvariant();
        var canonical = (input.CanonicalTerm ?? string.Empty).Trim();
        var variant = (input.VariantTerm ?? string.Empty).Trim();
        if (termType != HospitalAi.Application.Coding.Matching.TermSynonymTypes.Diagnosis
            && termType != HospitalAi.Application.Coding.Matching.TermSynonymTypes.Procedure)
        {
            throw new ValidationException("termType 只能是 DIAGNOSIS（诊断）或 PROCEDURE（手术）。");
        }

        if (canonical.Length == 0 || variant.Length == 0)
        {
            throw new ValidationException("规范词与等同词都不能为空。");
        }

        if (string.Equals(canonical, variant, StringComparison.Ordinal))
        {
            throw new ValidationException("等同词与规范词相同，无需登记。");
        }

        // (term_type, variant_term) 是库内唯一索引：一个等同词只能归属一个等价类。
        if (await db.IcdTermSynonyms.AnyAsync(
                item => item.TermType == termType && item.VariantTerm == variant,
                cancellationToken))
        {
            throw new ValidationException($"「{variant}」在 {termType} 通道已登记，请直接编辑该条或换用其它规范词。");
        }

        var now = DateTimeOffset.UtcNow;
        var record = new IcdTermSynonymRecord
        {
            Id = Guid.NewGuid(),
            TermType = termType,
            CanonicalTerm = canonical,
            VariantTerm = variant,
            IsEnabled = input.IsEnabled ?? true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.IcdTermSynonyms.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        synonymSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { id = record.Id }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeSynonymCreate));

// 单条同义词修改：改等同词/规范词、换通道或启停（null 字段沿用原值）。
app.MapPut(
    "/api/v1/knowledge/icd-term-synonym/{id:guid}",
    async (
        Guid id,
        IcdTermSynonymInput input,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.Matching.IIcdTermSynonymSource synonymSource,
        CancellationToken cancellationToken) =>
    {
        var record = await db.IcdTermSynonyms.FirstOrDefaultAsync(
            item => item.Id == id, cancellationToken);
        if (record is null)
        {
            return Results.NotFound();
        }

        var termType = string.IsNullOrWhiteSpace(input.TermType)
            ? record.TermType
            : input.TermType.Trim().ToUpperInvariant();
        if (termType != HospitalAi.Application.Coding.Matching.TermSynonymTypes.Diagnosis
            && termType != HospitalAi.Application.Coding.Matching.TermSynonymTypes.Procedure)
        {
            throw new ValidationException("termType 只能是 DIAGNOSIS（诊断）或 PROCEDURE（手术）。");
        }

        var canonical = string.IsNullOrWhiteSpace(input.CanonicalTerm)
            ? record.CanonicalTerm
            : input.CanonicalTerm.Trim();
        var variant = string.IsNullOrWhiteSpace(input.VariantTerm)
            ? record.VariantTerm
            : input.VariantTerm.Trim();
        if (canonical.Length == 0 || variant.Length == 0)
        {
            throw new ValidationException("规范词与等同词都不能为空。");
        }

        if (string.Equals(canonical, variant, StringComparison.Ordinal))
        {
            throw new ValidationException("等同词与规范词相同，无需登记。");
        }

        if (await db.IcdTermSynonyms.AnyAsync(
                item => item.Id != id && item.TermType == termType && item.VariantTerm == variant,
                cancellationToken))
        {
            throw new ValidationException($"「{variant}」在 {termType} 通道已登记，一个等同词只能归属一个等价类。");
        }

        record.TermType = termType;
        record.CanonicalTerm = canonical;
        record.VariantTerm = variant;
        record.IsEnabled = input.IsEnabled ?? record.IsEnabled;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        synonymSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { id = record.Id, updated = true }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeSynonymUpdate));

app.MapDelete(
    "/api/v1/knowledge/icd-term-synonym/{id:guid}",
    async (
        Guid id,
        HospitalAiDbContext db,
        HospitalAi.Application.Coding.Matching.IIcdTermSynonymSource synonymSource,
        CancellationToken cancellationToken) =>
    {
        var record = await db.IcdTermSynonyms.FirstOrDefaultAsync(
            item => item.Id == id, cancellationToken);
        if (record is null)
        {
            return Results.NotFound();
        }

        db.IcdTermSynonyms.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
        synonymSource.Invalidate();
        return Results.Ok(ApiEnvelope.Create(new { deleted = true }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeSynonymDelete));

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
    }).RequireAuthorization(RbacPolicies.CodingReader);

app.MapPost(
    "/api/v1/coding-tasks/{taskId:guid}/review",
    async (
        Guid taskId,
        ReviewCodingTaskRequest request,
        ICodingRecommendationService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        // 审核动作按按钮权限逐个校验：父权限 coding.review 视为全量审核权限。
        var requiredPermission = request.ReviewStatus?.Trim().ToUpperInvariant() switch
        {
            "ACCEPTED" => AppPermissions.CodingReviewAccept,
            "MODIFIED" => AppPermissions.CodingReviewModify,
            "REJECTED" => AppPermissions.CodingReviewReject,
            _ => null
        };
        if (requiredPermission is not null
            && !RbacPolicies.HasPermission(httpContext.User, requiredPermission)
            && !RbacPolicies.HasPermission(httpContext.User, AppPermissions.CodingReview))
        {
            return Results.Forbid();
        }

        var response = await service.ReviewAsync(taskId, request, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

app.MapGet(
    "/api/v1/workbench/tasks",
    async (
        string? status,
        ICodingRecommendationService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.ListWorkbenchTasksAsync(status, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReader);

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
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeHospitalCreate));

app.MapPost(
    "/api/v1/patients",
    async (
        CreatePatientRequest request,
        IReferenceDataService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreatePatientAsync(request, cancellationToken);
        return Results.Created($"/api/v1/patients/{response.Id}", ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

app.MapPost(
    "/api/v1/visits",
    async (
        CreateVisitRequest request,
        IReferenceDataService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateVisitAsync(request, cancellationToken);
        return Results.Created($"/api/v1/visits/{response.Id}", ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

app.MapPost(
    "/api/v1/documents",
    async (
        CreateDocumentRequest request,
        IReferenceDataService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateDocumentAsync(request, cancellationToken);
        return Results.Created($"/api/v1/documents/{response.Id}", ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

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
    }).RequireAuthorization(RbacPolicies.CodingReviewer);

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
    }).RequireAuthorization(RbacPolicies.CodingReader);

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
    }).RequireAuthorization(RbacPolicies.CodingReader);

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

app.MapGet(
    "/api/v1/users",
    async (
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var users = await service.ListUsersAsync(cancellationToken);
        return Results.Ok(ApiEnvelope.Create(users));
    }).RequireAuthorization(RbacPolicies.UserAdmin);

app.MapPost(
    "/api/v1/users",
    async (
        CreateUserRequest request,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateUserAsync(request, cancellationToken);
        return Results.Created($"/api/v1/users/{response.Id}", ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserCreate));

app.MapGet(
    "/api/v1/roles",
    async (
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var roles = await service.ListRolesAsync(cancellationToken);
        return Results.Ok(ApiEnvelope.Create(roles));
    }).RequireAuthorization(RbacPolicies.UserAdmin);

app.MapPost(
    "/api/v1/roles",
    async (
        CreateRoleRequest request,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var response = await service.CreateRoleAsync(request, cancellationToken);
        return Results.Created($"/api/v1/roles/{response.Id}", ApiEnvelope.Create(response));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.RoleCreate));

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
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserRoleAssign));

app.MapDelete(
    "/api/v1/users/{userId:guid}/roles/{roleId:guid}",
    async (
        Guid userId,
        Guid roleId,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        await service.RemoveRoleAsync(userId, roleId, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(new { ok = true }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserRoleAssign));

app.MapGet(
    "/api/v1/users/{userId:guid}/roles",
    async (
        Guid userId,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var roles = await service.GetRolesByUserAsync(userId, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(roles));
    }).RequireAuthorization(RbacPolicies.UserAdmin);

app.MapPatch(
    "/api/v1/users/{userId:guid}/status",
    async (
        Guid userId,
        UpdateUserStatusRequest request,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        await service.SetUserStatusAsync(userId, request.Status, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(new { ok = true }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.UserStatus));

app.MapGet(
    "/api/v1/roles/{roleId:guid}/permissions",
    async (
        Guid roleId,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        var permissions = await service.GetRolePermissionsAsync(roleId, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(permissions));
    }).RequireAuthorization(RbacPolicies.UserAdmin);

app.MapPut(
    "/api/v1/roles/{roleId:guid}/permissions",
    async (
        Guid roleId,
        UpdateRolePermissionsRequest request,
        ISecurityService service,
        CancellationToken cancellationToken) =>
    {
        await service.SetRolePermissionsAsync(roleId, request.Permissions, cancellationToken);
        return Results.Ok(ApiEnvelope.Create(new { ok = true }));
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.RolePermission));

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
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.CodingCaseEntry));

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
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.CodingReviewAccept));

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
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.CodingReviewReject));

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
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.CodingReviewModify));

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
    }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeIndexRebuild));

// 索引库编辑端点共用：空白视为未填（null），其余去除首尾空白。
static string? TrimToNull(string? value)
    => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

// 父链防环：新父节点不能是自己或自己的子孙（沿新父节点向上走，遇到自身即环）。
static async Task EnsureNoIndexParentCycleAsync(
    int recordSeqNo,
    int parentSeqNo,
    Func<int, Task<int?>> readParentSeqNo,
    CancellationToken cancellationToken)
{
    var cursor = parentSeqNo;
    for (var hops = 0; cursor > 0 && hops < 32; hops++)
    {
        if (cursor == recordSeqNo)
            throw new ValidationException("父节点不能是自身或其子孙节点。");
        var next = await readParentSeqNo(cursor);
        if (next is null)
            break;
        cursor = next.Value;
    }
}

app.Run();

/// <summary>
/// 供 WebApplicationFactory 在 API 集成测试中定位顶级语句生成的宿主类型。
/// </summary>
public partial class Program
{
}
