using HospitalAi.Api;
using HospitalAi.Api.Endpoints;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Worker.Pipeline;
using HospitalAi.Infrastructure.Outbox;
using HospitalAi.Api.Observability;
using HospitalAi.Api.Security;
using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Application.Coding.NameLookup;
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
using HospitalAi.Infrastructure.CodingLookup;
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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using System.Threading.RateLimiting;

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
builder.Services.AddScoped<OutboxStore>();
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
// "编码录入"名称解析：复用平台级索引/字典快照（AddCodingTaskPipeline 已注册），无状态即时解析。
builder.Services.AddScoped<INameCodingLookupService, NameCodingLookupService>();
// 标题通道开关与流水线运行时配置同源（pipeline_runtime_config "v22lite"，基线 V22PipelineOptions）。
builder.Services.AddScoped<
    HospitalAi.Application.Coding.NameLookup.ITitleChannelConfigSource,
    RuntimeTitleChannelConfigSource>();
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
// 登录限流（Security:RateLimit）：按客户端 IP 分区的固定窗口，防登录接口被刷（锁定为账号级兜底）。
var loginRateLimit = builder.Configuration
    .GetSection(LoginRateLimitOptions.SectionName)
    .Get<LoginRateLimitOptions>() ?? new LoginRateLimitOptions();

// 可信反向代理白名单：只有这些地址带来的 X-Forwarded-For 才被采信。
// 同时用于 ForwardedHeadersOptions（让中间件知道谁是代理）与限流分区键判定。
var trustedProxies = builder.Configuration
    .GetSection("Security:ForwardedHeaders:KnownProxies")
    .Get<string[]>() ?? [];
HospitalAi.Api.Security.LoginRateLimitClientKey.ConfigureTrustedProxies(trustedProxies);

if (loginRateLimit.LoginPermitLimit > 0)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                // 分区键取「转发头中间件处理前」的对端 IP（见 LoginRateLimitClientKey 注释）；
                // 医院内网 NAT 由账号级锁定策略兜底。
                HospitalAi.Api.Security.LoginRateLimitClientKey.Resolve(httpContext),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = loginRateLimit.LoginPermitLimit,
                    Window = TimeSpan.FromSeconds(Math.Max(loginRateLimit.LoginWindowSeconds, 1)),
                    QueueLimit = 0
                }));
    });
}

// 接口文档（WP-B2-2）：Minimal API 元数据 + Swashbuckle UI；中间件仅在非生产环境挂载。
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// OpenTelemetry 指标：HTTP/SQL/运行时 + Prometheus 端点（http://localhost:5080/metrics）。
builder.Services.AddHospitalAiApiMetrics("hospitalai.api");
if (builder.Configuration.GetValue<bool>("RabbitMq:Enabled"))
{
    builder.Services.AddRabbitMqPublisher(builder.Configuration);
}

// 前端管理应用跨域（开发环境前端运行在 3001+，生产走反代同源则无需 CORS）。
// 生产：Security:CorsOrigins 配置允许的源列表；同源部署时留空 = 仅同源请求（无跨域）。
var corsOrigins = builder.Configuration.GetSection("Security:CorsOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("admin", policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else if (builder.Environment.IsDevelopment())
        {
            policy
                .SetIsOriginAllowed(origin =>
                    origin.StartsWith("http://localhost:")
                    || origin.StartsWith("http://127.0.0.1:"))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        // 生产且未配置 origins：不加允许源（同源请求不受 CORS 约束）。
    }));

// DataProtection 密钥环持久化（WP-D）：容器重建不掉线。
// net10 targeting pack 裁剪了 PersistKeysToFileSystem 扩展方法（运行时存在、编译期不可见），
// 改用 PostConfigure 直接注入 FileSystemXmlRepository（等效于扩展方法内部实现）。
// 注意：必须在 builder.Build() 之前注册服务。
var dpKeysPath = builder.Configuration["DataProtection:KeysPath"];
builder.Services.AddDataProtection().SetApplicationName("HospitalAi");
if (!string.IsNullOrWhiteSpace(dpKeysPath))
{
    var keysDir = new System.IO.DirectoryInfo(dpKeysPath);
    builder.Services.PostConfigure<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>(
        options => options.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(keysDir, NullLoggerFactory.Instance));
}

var app = builder.Build();

// 对端 IP 采集：必须排在 UseForwardedHeaders 之前。
// ForwardedHeadersMiddleware 会用 X-Forwarded-For 覆盖 Connection.RemoteIpAddress，
// 而那个头完全由客户端控制。先把 TCP 对端地址存下来，限流分区键才有不可伪造的来源。
app.Use(async (context, next) =>
{
    HospitalAi.Api.Security.LoginRateLimitClientKey.CapturePeerIp(context);
    await next();
});

// 反向代理转发头（WP-C）：使 X-Forwarded-For / X-Forwarded-Proto 可信
// （登录限流按真实客户端 IP 分区）。
//
// 关键：必须清空 KnownIPNetworks 与 KnownProxies 的默认值。
// ASP.NET Core 10 的 ForwardedHeadersOptions 默认把 127.0.0.0/8 列入 KnownIPNetworks、
// 把 ::1 列入 KnownProxies，于是本机发来的请求能用 X-Forwarded-For 改写来源 IP。
// 清空后只有显式配置的 KnownProxies 才会被中间件采信。
var forwardedOptions = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
};
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
foreach (var proxy in trustedProxies)
{
    if (System.Net.IPAddress.TryParse(proxy, out var ip))
    {
        forwardedOptions.KnownProxies.Add(ip);
    }
}
app.UseForwardedHeaders(forwardedOptions);


if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HospitalAiDbContext>();
    await dbContext.Database.MigrateAsync();
}

// HSTS（WP-C）：生产强制 HTTPS（反向代理终结 TLS，应用侧只发头）。
if (app.Environment.IsProduction())
{
    app.UseHsts();
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
// 登录限流中间件：仅在启用限流时注册（AddRateLimiter 未调用时 UseRateLimiter 会抛异常）。
if (loginRateLimit.LoginPermitLimit > 0)
{
    app.UseRateLimiter();
}
app.UseMiddleware<RequestLoggingMiddleware>();
// 接口文档（WP-B2-2）：/swagger 仅开发环境暴露。
// 原条件 !IsProduction() 是黑名单式 —— 环境变量误设为 Development/Staging 时
// 会把完整 API 文档暴露在生产。改成 IsDevelopment() 白名单式：默认关闭，显式开开发才开。
if (builder.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
// OpenTelemetry Prometheus 端点，供监控抓取 HTTP/SQL/运行时指标。
app.UseOpenTelemetryPrometheusScrapingEndpoint();


// ===== 端点注册（按域拆分至 Endpoints/*.cs，注册顺序与拆分前一致） =====
app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapKnowledgeEndpoints();
app.MapWorkbenchEndpoints();
app.MapAdminEndpoints();
app.MapReferenceEndpoints();
app.MapTaskEndpoints();
app.MapSecurityEndpoints();
app.MapCodingEndpoints();


app.Run();

/// <summary>
/// 供 WebApplicationFactory 在 API 集成测试中定位顶级语句生成的宿主类型。
/// </summary>
public partial class Program
{
}

public sealed record PipelineConfigResponse(
    bool IcdTitleDirect,
    bool Cm3TitleDirect,
    bool IcdTitleReverse,
    bool VectorEnabled,
    bool RerankEnabled,
    bool RuleEngineEnabled,
    DateTimeOffset? UpdatedAt);

public sealed record UpdatePipelineConfigRequest(
    bool IcdTitleDirect,
    bool Cm3TitleDirect,
    bool IcdTitleReverse,
    bool? VectorEnabled,
    bool? RerankEnabled,
    bool? RuleEngineEnabled);

public sealed record PipelineRerunResponse(
    int RerunCount);
