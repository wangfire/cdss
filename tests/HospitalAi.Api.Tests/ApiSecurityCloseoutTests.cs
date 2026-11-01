using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using HospitalAi.Application.Security;
using HospitalAi.Contracts.Hospitals;
using HospitalAi.Contracts.Security;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace HospitalAi.Api.Tests;

/// <summary>
/// RBAC 收口集成测试：鉴权全覆盖（401/403）、登录锁定与解锁、内置角色模板。
/// 覆盖断言对应阶段计划 WP-B2-5「Api.Tests 断言 401/403」与登录锁定/限流待办。
/// </summary>
public sealed class ApiSecurityCloseoutTests
{
    private static readonly (HttpMethod Method, string Path)[] ProtectedEndpoints =
    [
        (HttpMethod.Get, "/api/v1/auth/me"),
        (HttpMethod.Post, "/api/v1/auth/change-password"),
        (HttpMethod.Post, "/api/v1/code-systems/import"),
        (HttpMethod.Post, "/api/v1/coding-rules/import"),
        (HttpMethod.Get, "/api/v1/knowledge/code-systems"),
        (HttpMethod.Get, "/api/v1/knowledge/medical-codes"),
        (HttpMethod.Get, "/api/v1/knowledge/term-synonyms"),
        (HttpMethod.Get, "/api/v1/knowledge/coding-rules"),
        (HttpMethod.Post, "/api/v1/icd-index/import"),
        (HttpMethod.Get, "/api/v1/knowledge/icd-index"),
        (HttpMethod.Get, "/api/v1/knowledge/icd-index/children"),
        (HttpMethod.Post, "/api/v1/knowledge/icd-index"),
        (HttpMethod.Put, $"/api/v1/knowledge/icd-index/{Guid.NewGuid()}"),
        (HttpMethod.Delete, $"/api/v1/knowledge/icd-index/{Guid.NewGuid()}"),
        (HttpMethod.Post, "/api/v1/cm3-index/import"),
        (HttpMethod.Get, "/api/v1/knowledge/cm3-index"),
        (HttpMethod.Get, "/api/v1/knowledge/cm3-index/children"),
        (HttpMethod.Post, "/api/v1/knowledge/cm3-index"),
        (HttpMethod.Put, $"/api/v1/knowledge/cm3-index/{Guid.NewGuid()}"),
        (HttpMethod.Delete, $"/api/v1/knowledge/cm3-index/{Guid.NewGuid()}"),
        (HttpMethod.Get, "/api/v1/knowledge/tumor-index"),
        (HttpMethod.Get, "/api/v1/knowledge/tumor-index/children"),
        (HttpMethod.Post, "/api/v1/knowledge/tumor-index"),
        (HttpMethod.Put, $"/api/v1/knowledge/tumor-index/{Guid.NewGuid()}"),
        (HttpMethod.Delete, $"/api/v1/knowledge/tumor-index/{Guid.NewGuid()}"),
        (HttpMethod.Post, "/api/v1/icd-term-synonym/import"),
        (HttpMethod.Get, "/api/v1/knowledge/icd-term-synonym"),
        (HttpMethod.Post, "/api/v1/knowledge/icd-term-synonym"),
        (HttpMethod.Put, $"/api/v1/knowledge/icd-term-synonym/{Guid.NewGuid()}"),
        (HttpMethod.Delete, $"/api/v1/knowledge/icd-term-synonym/{Guid.NewGuid()}"),
        (HttpMethod.Get, $"/api/v1/coding-tasks/{Guid.NewGuid()}/recommendations"),
        (HttpMethod.Post, $"/api/v1/coding-tasks/{Guid.NewGuid()}/review"),
        (HttpMethod.Get, "/api/v1/workbench/tasks"),
        (HttpMethod.Post, "/api/v1/hospitals"),
        (HttpMethod.Post, "/api/v1/patients"),
        (HttpMethod.Post, "/api/v1/visits"),
        (HttpMethod.Post, "/api/v1/documents"),
        (HttpMethod.Post, "/api/v1/coding-tasks"),
        (HttpMethod.Get, $"/api/v1/coding-tasks/{Guid.NewGuid()}"),
        (HttpMethod.Get, $"/api/v1/traces/{Guid.NewGuid()}"),
        (HttpMethod.Get, $"/api/v1/coding-tasks/{Guid.NewGuid()}/traces"),
        (HttpMethod.Get, "/api/v1/users"),
        (HttpMethod.Post, "/api/v1/users"),
        (HttpMethod.Get, "/api/v1/roles"),
        (HttpMethod.Post, "/api/v1/roles"),
        (HttpMethod.Post, $"/api/v1/users/{Guid.NewGuid()}/roles/{Guid.NewGuid()}"),
        (HttpMethod.Delete, $"/api/v1/users/{Guid.NewGuid()}/roles/{Guid.NewGuid()}"),
        (HttpMethod.Get, $"/api/v1/users/{Guid.NewGuid()}/roles"),
        (HttpMethod.Patch, $"/api/v1/users/{Guid.NewGuid()}/status"),
        (HttpMethod.Post, $"/api/v1/users/{Guid.NewGuid()}/unlock"),
        (HttpMethod.Get, "/api/v1/security/role-templates"),
        (HttpMethod.Get, $"/api/v1/roles/{Guid.NewGuid()}/permissions"),
        (HttpMethod.Put, $"/api/v1/roles/{Guid.NewGuid()}/permissions"),
        (HttpMethod.Get, $"/api/v1/visits/{Guid.NewGuid()}/readiness"),
        (HttpMethod.Get, $"/api/v1/visits/{Guid.NewGuid()}/clinical-facts"),
        (HttpMethod.Get, $"/api/v1/visits/{Guid.NewGuid()}/documents"),
        (HttpMethod.Get, $"/api/v1/visits/{Guid.NewGuid()}/coding"),
        (HttpMethod.Post, "/api/v1/coding/case-entry"),
        (HttpMethod.Post, $"/api/v1/coding/tasks/{Guid.NewGuid()}/diagnoses:batch"),
        (HttpMethod.Post, $"/api/v1/coding/diagnosis-inputs/{Guid.NewGuid()}/recommend"),
        (HttpMethod.Get, $"/api/v1/coding/diagnosis-inputs/{Guid.NewGuid()}/recommendations"),
        (HttpMethod.Get, $"/api/v1/coding/recommendations/{Guid.NewGuid()}"),
        (HttpMethod.Post, $"/api/v1/reviews/{Guid.NewGuid()}/accept"),
        (HttpMethod.Post, $"/api/v1/reviews/{Guid.NewGuid()}/reject"),
        (HttpMethod.Post, $"/api/v1/reviews/{Guid.NewGuid()}/modify"),
        (HttpMethod.Post, "/api/v1/final-results/submit"),
        (HttpMethod.Post, "/api/v1/admin/knowledge/index/rebuild"),
    ];

    // 供 403 断言使用的写端点抽样：分别落在知识库 / 审核按钮 / 账户 / 建任务四个授权面。
    private static readonly (HttpMethod Method, string Path)[] WriteEndpointSamples =
    [
        (HttpMethod.Post, "/api/v1/code-systems/import"),
        (HttpMethod.Post, "/api/v1/icd-index/import"),
        (HttpMethod.Post, "/api/v1/cm3-index/import"),
        (HttpMethod.Post, "/api/v1/knowledge/icd-index"),
        (HttpMethod.Delete, $"/api/v1/knowledge/icd-index/{Guid.NewGuid()}"),
        (HttpMethod.Post, "/api/v1/knowledge/cm3-index"),
        (HttpMethod.Delete, $"/api/v1/knowledge/cm3-index/{Guid.NewGuid()}"),
        (HttpMethod.Post, "/api/v1/knowledge/tumor-index"),
        (HttpMethod.Delete, $"/api/v1/knowledge/tumor-index/{Guid.NewGuid()}"),
        (HttpMethod.Post, "/api/v1/knowledge/icd-term-synonym"),
        (HttpMethod.Delete, $"/api/v1/knowledge/icd-term-synonym/{Guid.NewGuid()}"),
        (HttpMethod.Post, "/api/v1/reviews/{8C5B1111-1111-1111-1111-111111111111}/accept"),
        (HttpMethod.Post, "/api/v1/reviews/{8C5B1111-1111-1111-1111-111111111111}/modify"),
        (HttpMethod.Post, "/api/v1/reviews/{8C5B1111-1111-1111-1111-111111111111}/reject"),
        (HttpMethod.Post, "/api/v1/users"),
        (HttpMethod.Patch, "/api/v1/users/{8C5B1111-1111-1111-1111-111111111111}/status"),
        (HttpMethod.Post, "/api/v1/roles"),
        (HttpMethod.Put, "/api/v1/roles/{8C5B1111-1111-1111-1111-111111111111}/permissions"),
        (HttpMethod.Post, $"/api/v1/users/{Guid.NewGuid()}/unlock"),
        (HttpMethod.Post, "/api/v1/hospitals"),
        (HttpMethod.Post, "/api/v1/coding-tasks"),
        (HttpMethod.Post, "/api/v1/coding/case-entry"),
        (HttpMethod.Post, "/api/v1/final-results/submit"),
        (HttpMethod.Post, "/api/v1/admin/knowledge/index/rebuild"),
    ];

    [Fact]
    public async Task 受保护端点_匿名请求一律401()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        foreach (var (method, path) in ProtectedEndpoints)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch)
            {
                request.Content = JsonContent.Create(new { });
            }

            using var response = await client.SendAsync(request);

            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"匿名请求 {method} {path} 应返回 401，实际 {response.StatusCode}");
        }
    }

    [Fact]
    public async Task 只读权限令牌_写端点一律403()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        using var factory = CreateFactory(database.ConnectionString);
        // 只授 coding.read（带 permv 版本声明 = 新权限模型令牌），不含任何写权限。
        using var client = CreateTokenClient(
            factory,
            permissions: [AppPermissions.CodingRead]);

        foreach (var (method, path) in WriteEndpointSamples)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Content = JsonContent.Create(new { });

            using var response = await client.SendAsync(request);

            Assert.True(
                response.StatusCode == HttpStatusCode.Forbidden,
                $"只读令牌 {method} {path} 应返回 403，实际 {response.StatusCode}");
        }
    }

    [Fact]
    public async Task 无权限令牌_读端点403而非放行()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        using var factory = CreateFactory(database.ConnectionString);
        using var client = CreateTokenClient(factory, permissions: []);

        foreach (var path in new[]
                 {
                     "/api/v1/workbench/tasks",
                     "/api/v1/knowledge/icd-index",
                     $"/api/v1/coding-tasks/{Guid.NewGuid()}"
                 })
        {
            using var response = await client.GetAsync(path);

            Assert.True(
                response.StatusCode == HttpStatusCode.Forbidden,
                $"零权限令牌 GET {path} 应返回 403，实际 {response.StatusCode}");
        }
    }

    [Fact]
    public async Task 登录失败达阈值_账号锁定返回423_管理员可解锁()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        var logger = new CaptureLogger();
        using var factory = CreateFactory(database.ConnectionString, logger);
        using var bootstrapClient = CreateAdminClient(factory);

        var hospital = await CreateHospitalAsync(bootstrapClient, logger);
        var userName = $"u{Guid.NewGuid():N}"[..12];

        // 建用户（初始密码 Code+"123"）：管理请求需携带医院上下文（X-Hospital-Id）。
        using var adminClient = CreateHospitalClient(factory, hospital.Id);
        using var createResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/users",
            new CreateUserRequest(userName, "锁定测试用户"));
        createResponse.EnsureSuccessStatusCode();

        using var anonymous = factory.CreateClient();

        // 连续 4 次失败：仍返回 400（本系统密码错误口径为 validation_error）。
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var statusCode = await TryLoginAsync(anonymous, hospital.Id, userName, "wrong-password");
            Assert.True(
                statusCode == HttpStatusCode.BadRequest,
                $"第 {attempt} 次失败登录应 400，实际 {statusCode}");
        }

        // 第 5 次失败触发锁定：返回 423 Locked。
        Assert.Equal(
            HttpStatusCode.Locked,
            await TryLoginAsync(anonymous, hospital.Id, userName, "wrong-password"));

        // 锁定后即使密码正确也 423（先于密码校验）。
        Assert.Equal(
            HttpStatusCode.Locked,
            await TryLoginAsync(anonymous, hospital.Id, userName, $"{userName}123"));

        // 管理员看到锁定状态，解锁后即可用正确密码登录。
        var users = await ReadEnvelopeDataAsync<AppUserSummaryResponse[]>(
            await adminClient.GetAsync("/api/v1/users"));
        var target = users.Single(item => item.Code == userName);
        Assert.True(target.Lockout?.Locked, "5 次失败后用户应处于锁定状态");

        // 登录失败与锁定触发也应留痕（暴力破解溯源）：4 次普通失败 + 1 次锁定触发。
        await using var verifyContext = database.CreateContext();
        var failAuditCount = await verifyContext.AuditLogs
            .CountAsync(
                item => item.ResourceType == "app_user"
                    && item.ResourceId == target.Id
                    && (item.Action == "app_user.login_failed" || item.Action == "app_user.lockout_triggered"));
        Assert.True(failAuditCount >= 5, $"失败+锁定审计应 ≥5 条，实际 {failAuditCount}");
        var lockoutAudit = await verifyContext.AuditLogs
            .SingleAsync(
                item => item.ResourceType == "app_user"
                    && item.ResourceId == target.Id
                    && item.Action == "app_user.lockout_triggered");
        Assert.Equal("LOCKED", lockoutAudit.Result);
        Assert.Equal("FAILED", (await verifyContext.AuditLogs
            .FirstAsync(item => item.Action == "app_user.login_failed")).Result);

        using var unlockResponse = await adminClient.PostAsync(
            $"/api/v1/users/{target.Id}/unlock", content: null);
        unlockResponse.EnsureSuccessStatusCode();

        // 解锁动作必须落审计日志（操作者 + 被解锁账号），可追溯绕过锁定的运维行为。
        var audit = await verifyContext.AuditLogs
            .SingleAsync(
                item => item.ResourceType == "app_user"
                    && item.ResourceId == target.Id
                    && item.Action == "app_user.unlock");
        Assert.Equal("SUCCESS", audit.Result);
        Assert.Equal("api-test-user", audit.ActorId);

        var loginStatusCode = await TryLoginAsync(
            anonymous, hospital.Id, userName, $"{userName}123");
        Assert.True(
            loginStatusCode == HttpStatusCode.OK,
            $"解锁后正确密码登录应 200，实际 {loginStatusCode}");
    }

    [Fact]
    public async Task 登录成功_失败计数清零不累积锁定()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        var logger = new CaptureLogger();
        using var factory = CreateFactory(database.ConnectionString, logger);
        using var bootstrapClient = CreateAdminClient(factory);

        var hospital = await CreateHospitalAsync(bootstrapClient, logger);
        var userName = $"u{Guid.NewGuid():N}"[..12];

        using var adminClient = CreateHospitalClient(factory, hospital.Id);
        using var createResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/users",
            new CreateUserRequest(userName, "计数清零测试用户"));
        createResponse.EnsureSuccessStatusCode();
        var correctPassword = $"{userName}123";

        using var anonymous = factory.CreateClient();

        // 2 次失败 → 成功 → 2 次失败：成功后计数清零，不应达到 5 次阈值。
        await TryLoginAsync(anonymous, hospital.Id, userName, "wrong-1");
        await TryLoginAsync(anonymous, hospital.Id, userName, "wrong-2");
        Assert.Equal(
            HttpStatusCode.OK,
            await TryLoginAsync(anonymous, hospital.Id, userName, correctPassword));
        await TryLoginAsync(anonymous, hospital.Id, userName, "wrong-3");
        var last = await TryLoginAsync(anonymous, hospital.Id, userName, "wrong-4");

        Assert.True(
            last == HttpStatusCode.BadRequest,
            $"清零后再失败 2 次不应锁定，实际 {last}");

        // 正确密码仍可登录（未锁定）。
        Assert.Equal(
            HttpStatusCode.OK,
            await TryLoginAsync(anonymous, hospital.Id, userName, correctPassword));
    }

    [Fact]
    public async Task 内置角色模板_列表可见_套用创建即带权限()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        var logger = new CaptureLogger();
        using var factory = CreateFactory(database.ConnectionString, logger);
        using var bootstrapClient = CreateAdminClient(factory);

        var hospital = await CreateHospitalAsync(bootstrapClient, logger);
        using var adminClient = CreateHospitalClient(factory, hospital.Id);

        // 模板列表：5 个内置模板，权限集与 RoleMatrix 一致。
        var templates = await ReadEnvelopeDataAsync<RoleTemplateResponse[]>(
            await adminClient.GetAsync("/api/v1/security/role-templates"));
        Assert.Equal(RoleMatrix.BuiltIn.Count, templates.Length);
        Assert.Contains(
            templates,
            item => item.Code == RoleMatrix.Coder.Code
                && item.Permissions.Count == RoleMatrix.Coder.Permissions.Count);
        Assert.Contains(
            templates,
            item => item.Code == RoleMatrix.UserAdmin.Code
                && !item.Permissions.Contains(AppPermissions.RolePermission));

        // 套用模板创建角色：以模板码 CODER 创建即带模板权限（无需二次配置）。
        using var createResponse = await adminClient.SendAsync(new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/roles")
        {
            Content = JsonContent.Create(
                new CreateRoleRequest(
                    RoleMatrix.Coder.Code,
                    "编码员（重建）",
                    "来自 RoleMatrix 模板"))
        });
        createResponse.EnsureSuccessStatusCode();
        var role = await ReadEnvelopeDataAsync<AppRoleResponse>(createResponse);

        Assert.Equal(RoleMatrix.Coder.Permissions, role.Permissions);

        // 人工收敛：整体替换权限集仍然生效（模板只影响创建初值）。
        var update = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/roles/{role.Id}/permissions")
        {
            Content = JsonContent.Create(
                new UpdateRolePermissionsRequest([AppPermissions.CodingRead]))
        };
        using var updateResponse = await adminClient.SendAsync(update);
        updateResponse.EnsureSuccessStatusCode();

        var permissions = await ReadEnvelopeDataAsync<string[]>(
            await adminClient.GetAsync($"/api/v1/roles/{role.Id}/permissions"));
        Assert.Equal([AppPermissions.CodingRead], permissions);
    }

    [Fact]
    public async Task 登录限流_窗口内超限返回429统一错误体()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        var logger = new CaptureLogger();
        using var factory = CreateFactory(database.ConnectionString, logger)
            .WithWebHostBuilder(builder => builder.UseSetting("Security:RateLimit:LoginPermitLimit", "3"));
        using var anonymous = factory.CreateClient();

        var loginRequest = new
        {
            userName = "rate-limit-user",
            password = "whatever",
            hospitalId = Guid.NewGuid()
        };

        // 窗口内前 3 个请求放行（400 业务错误：用户不存在），第 4 个起 429。
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var response = await anonymous.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, statuses[^2]);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, statuses[0]);
    }

    // ===== 辅助 =====

    private static async Task<HttpStatusCode> TryLoginAsync(
        HttpClient client,
        Guid hospitalId,
        string userName,
        string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(userName, password, hospitalId));
        return response.StatusCode;
    }

    private static HttpClient CreateAdminClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            CdssTestFactory.CreateBearerToken());
        return client;
    }

    /// <summary>管理员客户端 + 医院上下文：用户/角色管理端点要求 X-Hospital-Id 隔离。</summary>
    private static HttpClient CreateHospitalClient(
        WebApplicationFactory<Program> factory,
        Guid hospitalId)
    {
        var client = CreateAdminClient(factory);
        client.DefaultRequestHeaders.Add("X-Hospital-Id", hospitalId.ToString());
        return client;
    }

    /// <summary>
    /// 签发带权限声明（permv 版本）的测试令牌：走真实权限码判定，
    /// 不经过旧角色码回退，专测权限矩阵语义。
    /// </summary>
    private static HttpClient CreateTokenClient(
        WebApplicationFactory<Program> factory,
        IReadOnlyList<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "perm-test-user"),
            new("sub", "perm-test-user"),
            new("hospitalId", Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(AppClaimTypes.PermissionVersion, AppClaimTypes.PermissionVersionValue),
        };
        claims.AddRange(permissions.Select(permission =>
            new Claim(AppClaimTypes.Permission, permission)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(CdssTestFactory.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "hospitalai",
            audience: "hospitalai-admin",
            claims: claims,
            notBefore: DateTime.UtcNow.AddSeconds(-30),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);
        var encoded = new JwtSecurityTokenHandler { MapInboundClaims = false }.WriteToken(token);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", encoded);
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        CaptureLogger? logger = null)
    {
        return new CdssTestFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:HospitalAi", connectionString);
                builder.ConfigureAppConfiguration(configuration =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:HospitalAi"] = connectionString,
                        ["RabbitMq:Enabled"] = "false",
                        ["HealthChecks:Elasticsearch:Host"] = "127.0.0.1",
                        ["HealthChecks:Elasticsearch:Port"] = "6390",
                        ["HealthChecks:RabbitMq:Host"] = "127.0.0.1",
                        ["HealthChecks:RabbitMq:Port"] = "5673"
                    });
                });
                builder.ConfigureLogging(loggingBuilder =>
                {
                    if (logger is not null)
                    {
                        loggingBuilder.AddProvider(new SingleLoggerProvider(logger));
                    }
                });
            });
    }

    private static async Task<HospitalResponse> CreateHospitalAsync(
        HttpClient client,
        CaptureLogger? logger = null)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new CreateHospitalRequest($"H-{Guid.NewGuid():N}"[..16], "Security Test Hospital"));
        if (logger is not null)
        {
            await EnsureSuccessAsync(response, logger);
        }
        else
        {
            response.EnsureSuccessStatusCode();
        }

        return await ReadEnvelopeDataAsync<HospitalResponse>(response);
    }

    private static async Task<T> ReadEnvelopeDataAsync<T>(HttpResponseMessage response)
    {
        var root = await response.Content.ReadFromJsonAsync<JsonElement>();
        return root.GetProperty("data")
            .Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CaptureLogger logger)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"{(int)response.StatusCode} {response.StatusCode}: {body}. Logs: {logger.Dump()}");
    }

    private sealed class CaptureLogger
    {
        private readonly object _lock = new();
        private readonly List<string> _entries = [];

        public void Add(string message)
        {
            lock (_lock)
            {
                _entries.Add(message);
            }
        }

        public string Dump()
        {
            lock (_lock)
            {
                return string.Join(" | ", _entries.TakeLast(20));
            }
        }
    }

    private sealed class SingleLoggerProvider(CaptureLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ForwardingLogger(logger);

        public void Dispose()
        {
        }
    }

    private sealed class ForwardingLogger(CaptureLogger logger) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            logger.Add(formatter(state, exception));
        }
    }

    private sealed class ApiTestDatabase : IAsyncDisposable
    {
        private readonly string _masterConnectionString;

        private ApiTestDatabase(
            string databaseName,
            string masterConnectionString,
            string connectionString)
        {
            DatabaseName = databaseName;
            _masterConnectionString = masterConnectionString;
            ConnectionString = connectionString;
        }

        private string DatabaseName { get; }

        public string ConnectionString { get; }

        public static async Task<ApiTestDatabase> CreateAsync()
        {
            var template = Environment.GetEnvironmentVariable(
                "HOSPITAL_AI_TEST_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new InvalidOperationException(
                    "运行 API 安全集成测试前必须设置 HOSPITAL_AI_TEST_CONNECTION_STRING。");
            }

            var databaseName = $"HospitalAi_Sec_{Guid.NewGuid():N}";
            var builder = new SqlConnectionStringBuilder(template)
            {
                InitialCatalog = databaseName
            };
            var masterBuilder = new SqlConnectionStringBuilder(builder.ConnectionString)
            {
                InitialCatalog = "master"
            };

            await using (var connection = new SqlConnection(masterBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{databaseName}];";
                await command.ExecuteNonQueryAsync();
            }

            var database = new ApiTestDatabase(
                databaseName,
                masterBuilder.ConnectionString,
                builder.ConnectionString);
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public HospitalAiDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<HospitalAiDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;
            return new HospitalAiDbContext(options);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var connection = new SqlConnection(_masterConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"IF EXISTS (SELECT 1 FROM sys.databases WHERE name = '{DatabaseName}') " +
                    $"BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                    $"DROP DATABASE [{DatabaseName}]; END";
                await command.ExecuteNonQueryAsync();
            }
            catch
            {
                // 清理失败不影响测试结果（临时库由连接串前缀标识，可人工清理）。
            }
        }
    }
}
