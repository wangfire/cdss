using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using HospitalAi.Api;
using HospitalAi.Contracts.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace HospitalAi.Api.Tests;

public sealed class ApiSmokeTests
    : IClassFixture<CdssTestFactory>
{
    private readonly HttpClient _client;

    public ApiSmokeTests(CdssTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_返回状态和TraceId()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/health/live");
        request.Headers.Add("X-Trace-Id", "api-test-trace");

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("api-test-trace", body?.TraceId);
        Assert.Equal("api-test-trace", response.Headers.GetValues("X-Trace-Id").Single());
    }

    [Fact]
    public async Task RequestContext_传播RequestId并生成TraceId()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/health/live");
        request.Headers.Add("X-Request-Id", "api-test-request");

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("api-test-request", response.Headers.GetValues("X-Request-Id").Single());
        Assert.False(string.IsNullOrWhiteSpace(body?.TraceId));
        Assert.Equal(body?.TraceId, response.Headers.GetValues("X-Trace-Id").Single());
    }

    [Fact]
    public async Task 未携带令牌访问受保护端点_返回401()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/coding-tasks")
        {
            Content = JsonContent.Create(new
            {
                visitId = Guid.NewGuid(),
                pipelineVersion = "pipeline-v1"
            })
        };
        request.Headers.Add("X-Trace-Id", "api-error-trace");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 无效Bearer令牌_返回401而非静默降级()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/coding-tasks")
        {
            Content = JsonContent.Create(new
            {
                visitId = Guid.NewGuid(),
                pipelineVersion = "pipeline-v1"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-token");

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", body?.Code);
    }

    [Fact]
    public async Task 创建任务缺少医院上下文_返回统一错误响应()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/coding-tasks")
        {
            Content = JsonContent.Create(new
            {
                visitId = Guid.NewGuid(),
                pipelineVersion = "pipeline-v1"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CdssTestFactory.CreateBearerToken());
        request.Headers.Add("X-Trace-Id", "api-error-trace");
        request.Headers.Add("Idempotency-Key", "api-error-idem");

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_error", body?.Code);
        Assert.Equal("api-error-trace", body?.TraceId);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Swagger_非生产环境暴露接口文档()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/v1/", body);
    }

    [Fact]
    public async Task Auth_Login_缺少字段返回验证错误()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { userName = "admin", password = "x" })
        };

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_error", body?.Code);
    }

    private sealed record HealthResponse(string Status, string TraceId);
}

/// <summary>
/// 共享测试宿主：固定 Testing 环境与 JWT 签名密钥，测试令牌用同一密钥签发，
/// 使集成测试走真实 Bearer 认证与 RBAC 策略（WP-B2-5 后端点已挂授权）。
/// </summary>
public sealed class CdssTestFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "TEST-ONLY-SIGNING-KEY-0123456789ABCDEF";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("JwtAuth:Key", SigningKey);
    }

    /// <summary>
    /// 签发与生产令牌同构的测试令牌：sub + 长名角色声明（MapInboundClaims=false 下
    /// ClaimTypes.Role 原样保留，IsInRole 可判定）。
    /// </summary>
    public static string CreateBearerToken(
        string userId = "api-test-user",
        IReadOnlyCollection<string>? roles = null,
        Guid? hospitalId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("sub", userId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (hospitalId is Guid hospital)
        {
            claims.Add(new Claim("hospitalId", hospital.ToString()));
        }

        claims.AddRange((roles ?? ["ADMIN"]).Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "hospitalai",
            audience: "hospitalai-admin",
            claims: claims,
            notBefore: DateTime.UtcNow.AddSeconds(-30),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler { MapInboundClaims = false }.WriteToken(token);
    }
}
