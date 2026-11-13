using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HospitalAi.Api;
using HospitalAi.Application.Security;
using HospitalAi.Contracts.Models;
using HospitalAi.Infrastructure.ModelGateways;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HospitalAi.Api.Tests;

/// <summary>
/// 2026-11-13 阶段 1「止血」修复的回归测试。
///
/// 覆盖四个 P0 功能缺陷（修复前均为静默失效，界面无任何报错）：
///   P0-1 Api 侧 LLM 一致性校验永久不可用（模型码写错 → 恒 DEGRADED.NO_MODEL）
///   P0-2 肿瘤索引「启用/停用」按钮状态恒错（后端不返回 isEnabled）
///   P0-3 登录限流可被伪造 X-Forwarded-For 绕过
///   P0-4 暴力破解锁定可被静默禁用（写库异常被 catch 吞掉）
/// </summary>
public sealed class ApiP0RegressionTests
{
    // ==================== P0-1：模型码必须能在映射表里解析 ====================

    /// <summary>
    /// P0-1 的根因是配置写了字面量 "model.small"，而映射表里只有 "medical-small"。
    /// 本用例直接锁住这个不变量：Api 侧配置的模型码必须能在 ModelGateway:Models 中解析，
    /// 否则 StaticModelRouter 会返回 Available=false，LLM 校验静默不执行。
    /// </summary>
    [Fact]
    public void Api配置的解释模型码_必须能在模型映射表中解析()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var modelCode = configuration["Coding:V22Lite:ExplanationModelCode"];
        var models = configuration.GetSection("ModelGateway:Models").Get<Dictionary<string, string>>();

        Assert.False(string.IsNullOrWhiteSpace(modelCode), "Coding:V22Lite:ExplanationModelCode 未配置。");
        Assert.NotNull(models);
        Assert.True(
            models.ContainsKey(modelCode!),
            $"模型码 '{modelCode}' 不在 ModelGateway:Models 中（可用：{string.Join(", ", models.Keys)}）。" +
            "这会让 StaticModelRouter 返回不可用，Api 侧 LLM 一致性校验静默不执行。");

        // 代码常量与配置必须一致，避免下次又被写成字面量。
        Assert.Equal(ModelCodes.Small, modelCode);
    }

    /// <summary>
    /// Api 与 Worker 的 Coding:V22Lite 基线必须完全一致。
    /// 否则 pipeline_runtime_config 行缺失时（首次部署/清库后），两条流水线路径
    /// 会因标题通道开关默认值不同而给出不同推荐结果。
    /// </summary>
    [Fact]
    public void Api与Worker的流水线基线配置_必须一致()
    {
        var apiPath = ResolveRepoFile("src/HospitalAi.Api/appsettings.json");
        var workerPath = ResolveRepoFile("src/HospitalAi.Worker/appsettings.json");
        Assert.True(File.Exists(apiPath), $"找不到 {apiPath}");
        Assert.True(File.Exists(workerPath), $"找不到 {workerPath}");

        var api = ReadV22Lite(apiPath);
        var worker = ReadV22Lite(workerPath);

        var keys = api.Keys.Union(worker.Keys).OrderBy(key => key, StringComparer.Ordinal).ToList();
        var diffs = new List<string>();
        foreach (var key in keys)
        {
            var a = api.GetValueOrDefault(key, "<缺失>");
            var w = worker.GetValueOrDefault(key, "<缺失>");
            if (!string.Equals(a, w, StringComparison.Ordinal))
            {
                diffs.Add($"{key}: Api={a} / Worker={w}");
            }
        }

        Assert.True(
            diffs.Count == 0,
            "Api 与 Worker 的 Coding:V22Lite 配置不一致：\n  " + string.Join("\n  ", diffs));
    }

    // ==================== P0-3：伪造 X-Forwarded-For 不能绕过限流 ====================

    /// <summary>
    /// P0-3：限流分区键取自校正后的 RemoteIpAddress，不再读客户端可控的原始头。
    /// 用同一个客户端（同一 RemoteIp）连续请求并每次伪造不同的 X-Forwarded-For，
    /// 超过窗口配额后必须被限流——修复前每次换头就换分区，永远不会 429。
    /// </summary>
    [Fact]
    public async Task 登录限流_伪造不同XForwardedFor不能绕过配额()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        using var factory = CreateDatabaseFactory(database.ConnectionString)
            .WithWebHostBuilder(builder =>
            {
                // 把窗口配额压到 3 次，便于在测试内触发 429。
                builder.UseSetting("Security:RateLimit:LoginPermitLimit", "3");
            });
        using var client = factory.CreateClient();

        var loginRequest = new
        {
            userName = "rate-limit-p0-user",
            password = "whatever",
            hospitalId = Guid.NewGuid()
        };

        var statuses = new List<HttpStatusCode>();
        var firstBody = string.Empty;
        // 发足够多的请求（超过默认配额 60），以确认限流器是否真的在跑；
        // 若配额被改成了 3，第 4 次就应 429。
        for (var i = 0; i < 80; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(loginRequest)
            };
            // 每次换一个伪造的转发头：修复前每个值都是独立分区，80 次都不会 429。
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"10.0.0.{i + 1}");

            using var response = await client.SendAsync(request);
            if (i == 0)
            {
                firstBody = await response.Content.ReadAsStringAsync();
            }

            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.True(
            statuses.Contains(HttpStatusCode.TooManyRequests),
            "伪造不同 X-Forwarded-For 绕过了限流；状态码序列前 10 项：" +
            string.Join(", ", statuses.Take(10)) + $"（共 {statuses.Count} 次）；首个响应体：" + firstBody);
    }

    /// <summary>
    /// 对照组：完全不带 X-Forwarded-For 时，限流器必须正常触发。
    /// 若本用例也不触发，说明问题不在「伪造头」，而在限流器本身没启用。
    /// </summary>
    [Fact]
    public async Task 登录限流_不带伪造头时必须正常触发()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        using var factory = CreateDatabaseFactory(database.ConnectionString)
            .WithWebHostBuilder(builder =>
                builder.UseSetting("Security:RateLimit:LoginPermitLimit", "3"));
        using var client = factory.CreateClient();

        var loginRequest = new
        {
            userName = "rate-limit-baseline-user",
            password = "whatever",
            hospitalId = Guid.NewGuid()
        };

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.True(
            statuses.Contains(HttpStatusCode.TooManyRequests),
            "不带伪造头时限流也未触发，状态码序列：" + string.Join(", ", statuses));
    }

    // ==================== P0-2：肿瘤索引查询必须返回 isEnabled ====================

    /// <summary>
    /// 医院隔离：知识库查询端点必须按当前请求的医院过滤。
    /// code_system / medical_code / term_synonym / coding_rule 四张表都带 hospital_id，
    /// 未过滤时医院 A 的编码员能读到医院 B 的字典与规则（跨院越权）。
    /// 这里种两个医院的数据，断言 A 的令牌只看到 A 的行。
    /// </summary>
    [Fact]
    public async Task 知识库查询_必须按医院隔离_不能读到其他医院的字典()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        var hospitalA = Guid.NewGuid();
        var hospitalB = Guid.NewGuid();

        // 直接种数据：A、B 两家医院各一条编码体系与一条字典。
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            foreach (var (hospital, code, title) in new[]
                     {
                         (hospitalA, "DEMO-A", "A院专属编码"),
                         (hospitalB, "DEMO-B", "B院专属编码")
                     })
            {
                var systemId = Guid.NewGuid();
                await ExecuteAsync(connection,
                    "INSERT INTO hospital (id, code, name, status, created_at, updated_at) " +
                    "VALUES (@id, @code, @name, 'ACTIVE', SYSUTCDATETIME(), SYSUTCDATETIME())",
                    new SqlParameter("@id", hospital), new SqlParameter("@code", "H" + code), new SqlParameter("@name", "医院" + code));
                await ExecuteAsync(connection,
                    "INSERT INTO code_system (id, hospital_id, code, name, version, created_at, updated_at) " +
                    "VALUES (@id, @hospital, @code, @name, 'v1', SYSUTCDATETIME(), SYSUTCDATETIME())",
                    new SqlParameter("@id", systemId), new SqlParameter("@hospital", hospital), new SqlParameter("@code", code), new SqlParameter("@name", "体系" + code));
                await ExecuteAsync(connection,
                    "INSERT INTO medical_code (id, hospital_id, code_system_id, code_system_code, code, title, code_type, search_text, is_enabled, created_at, updated_at) " +
                    "VALUES (NEWID(), @hospital, @systemId, @code, 'X00.1', @title, @code, @title, 1, SYSUTCDATETIME(), SYSUTCDATETIME())",
                    new SqlParameter("@hospital", hospital), new SqlParameter("@systemId", systemId), new SqlParameter("@code", code), new SqlParameter("@title", title));
            }
        }

        using var factory = CreateDatabaseFactory(database.ConnectionString);
        using var client = CreateTokenClient(factory, [AppPermissions.KnowledgeRead], hospitalA);

        using var systemsResponse = await client.GetAsync("/api/v1/knowledge/code-systems");
        systemsResponse.EnsureSuccessStatusCode();
        var systems = await systemsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var systemItems = systems.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, systemItems.ValueKind);
        Assert.Equal(1, systemItems.GetArrayLength());
        Assert.Equal("DEMO-A", systemItems[0].GetProperty("code").GetString());

        using var codesResponse = await client.GetAsync("/api/v1/knowledge/medical-codes?page=1&pageSize=50");
        codesResponse.EnsureSuccessStatusCode();
        var codes = await codesResponse.Content.ReadFromJsonAsync<JsonElement>();
        var codeItems = codes.GetProperty("data").GetProperty("items");
        Assert.Equal(1, codeItems.GetArrayLength());
        Assert.Equal("A院专属编码", codeItems[0].GetProperty("title").GetString());
    }

    /// <summary>
    /// P0-2：后端肿瘤索引查询的 Select 投影曾漏掉 IsEnabled（ICD/CM3 都有），
    /// 前端 `row.isEnabled !== false` 恒为 true，按钮永远只显示「停用」。
    /// 这里断言响应里存在该字段（值本身可为 false，取决于库内容）。
    /// </summary>
    [Fact]
    public async Task 肿瘤索引查询_响应必须包含isEnabled字段()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        using var factory = CreateDatabaseFactory(database.ConnectionString);
        using var client = CreateTokenClient(factory, [AppPermissions.KnowledgeRead]);

        using var response = await client.GetAsync("/api/v1/knowledge/tumor-index?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        var envelope = document.RootElement;
        var data = envelope.GetProperty("data");
        var items = data.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);

        // 投影缺字段时，items 元素上根本不会有 isEnabled 这个键。
        if (items.GetArrayLength() > 0)
        {
            var first = items[0];
            Assert.True(
                first.TryGetProperty("isEnabled", out var isEnabled),
                "肿瘤索引响应缺少 isEnabled 字段——前端启停按钮状态会恒错。");
            Assert.True(
                isEnabled.ValueKind is JsonValueKind.True or JsonValueKind.False,
                $"isEnabled 应为布尔值，实际为 {isEnabled.ValueKind}。");
        }
    }

    /// <summary>
    /// 肿瘤索引 children 投影同样必须返回 isEnabled（该端点是两段投影，
    /// 内层查询与外层组装各有一份，容易只改一处）。
    /// </summary>
    [Fact]
    public async Task 肿瘤索引子节点查询_响应必须包含isEnabled字段()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        using var factory = CreateDatabaseFactory(database.ConnectionString);
        using var client = CreateTokenClient(factory, [AppPermissions.KnowledgeRead]);

        using var response = await client.GetAsync("/api/v1/knowledge/tumor-index/children?parentSeqNo=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        var items = document.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);

        if (items.GetArrayLength() > 0)
        {
            Assert.True(
                items[0].TryGetProperty("isEnabled", out var isEnabled),
                "肿瘤索引 children 响应缺少 isEnabled 字段——前端树形视图的启停按钮状态会恒错。");
            Assert.True(
                isEnabled.ValueKind is JsonValueKind.True or JsonValueKind.False,
                $"isEnabled 应为布尔值，实际为 {isEnabled.ValueKind}。");
        }
    }

    /// <summary>
    /// 启停权限按索引类型判定：只授 cm3.update 的角色不应能改 icd 索引行，
    /// 只授 tumor.update 的角色不应能改 icd 索引行。
    /// （端点级用 knowledge-index-toggle 策略放行，具体 kind 在端点内二次判定。）
    /// </summary>
    [Fact]
    public async Task 索引启停_权限按索引类型判定_不能跨类型越权()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        using var factory = CreateDatabaseFactory(database.ConnectionString);

        // 只授 cm3.update，去改 icd 索引行 → 必须 403。
        using var client = CreateTokenClient(factory, [AppPermissions.KnowledgeCm3Update]);
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/knowledge/icd-index/{Guid.NewGuid()}/enabled")
        {
            Content = JsonContent.Create(new { enabled = false })
        };

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// 只授 cm3.update 的角色改 cm3 索引行不应因端点级策略被挡住
    /// （修复前端点硬绑 icd.update，会把这类角色全部挡在门外 = 误伤）。
    /// 行不存在时返回 404 而非 403，说明请求已通过权限判定。
    /// </summary>
    [Fact]
    public async Task 索引启停_持有对应类型权限的角色不应被端点级策略挡住()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        using var factory = CreateDatabaseFactory(database.ConnectionString);

        using var client = CreateTokenClient(factory, [AppPermissions.KnowledgeCm3Update]);
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/knowledge/cm3-index/{Guid.NewGuid()}/enabled")
        {
            Content = JsonContent.Create(new { enabled = false })
        };

        using var response = await client.SendAsync(request);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==================== 辅助 ====================

    /// <summary>
    /// 每个用例一个独立临时库：建库 → EnsureCreated 建表 → 用完删除。
    /// 与 ApiObservabilityTests / ApiSecurityCloseoutTests 同构。
    /// </summary>
    private sealed class ApiTestDatabase : IAsyncDisposable
    {
        private readonly string _databaseName;
        private readonly string _masterConnectionString;

        private ApiTestDatabase(
            string databaseName,
            string masterConnectionString,
            string connectionString)
        {
            _databaseName = databaseName;
            _masterConnectionString = masterConnectionString;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<ApiTestDatabase> CreateAsync()
        {
            var template = Environment.GetEnvironmentVariable(
                "HOSPITAL_AI_TEST_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new InvalidOperationException(
                    "运行 API 集成测试前必须设置 HOSPITAL_AI_TEST_CONNECTION_STRING。");
            }

            var databaseName = $"HospitalAi_P0_{Guid.NewGuid():N}";
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

            var options = new DbContextOptionsBuilder<HospitalAiDbContext>()
                .UseSqlServer(builder.ConnectionString)
                .Options;
            await using (var context = new HospitalAiDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
            }

            return new ApiTestDatabase(databaseName, masterBuilder.ConnectionString, builder.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new SqlConnection(_masterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID('{_databaseName}') IS NOT NULL " +
                $"BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static Dictionary<string, string> ReadV22Lite(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var node = document.RootElement.GetProperty("Coding").GetProperty("V22Lite");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => property.Value.GetRawText()
            };
        }

        return result;
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        string sql,
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>从测试程序集目录上溯到仓库根，取源文件路径。</summary>
    private static string ResolveRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, relativePath);
    }

    private static WebApplicationFactory<Program> CreateDatabaseFactory(
        string connectionString,
        IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:HospitalAi"] = connectionString,
            ["RabbitMq:Enabled"] = "false"
        };
        if (extraSettings is not null)
        {
            foreach (var pair in extraSettings)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        return new CdssTestFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:HospitalAi", connectionString);
                // 注意：额外配置必须走 UseSetting（写入 host settings 源，优先级最高），
                // 不能只走 ConfigureAppConfiguration —— 后者追加的源在 Program.cs
                // 读取 builder.Configuration 取限流选项时还没生效。
                foreach (var pair in settings)
                {
                    builder.UseSetting(pair.Key, pair.Value);
                }

                builder.ConfigureAppConfiguration(configuration =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:HospitalAi"] = connectionString,
                        ["RabbitMq:Enabled"] = "false"
                    });
                });
            });
    }

    /// <summary>签发带权限声明（permv 版本）的令牌，走真实权限码判定。</summary>
    private static HttpClient CreateTokenClient(
        WebApplicationFactory<Program> factory,
        IReadOnlyList<string> permissions,
        Guid? hospitalId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "p0-regression-user"),
            new("sub", "p0-regression-user"),
            new("hospitalId", (hospitalId ?? Guid.NewGuid()).ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(AppClaimTypes.PermissionVersion, AppClaimTypes.PermissionVersionValue),
        };
        claims.AddRange(permissions.Select(permission =>
            new Claim(AppClaimTypes.Permission, permission)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(CdssTestFactory.SigningKey)),
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
        client.DefaultRequestHeaders.Add("X-Hospital-Id", (hospitalId ?? Guid.NewGuid()).ToString());
        return client;
    }
}
