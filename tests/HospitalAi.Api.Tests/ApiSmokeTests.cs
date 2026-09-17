using System.Net;
using System.Net.Http.Json;
using HospitalAi.Contracts.Common;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HospitalAi.Api.Tests;

public sealed class ApiSmokeTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
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
        request.Headers.Add("X-Trace-Id", "api-error-trace");
        request.Headers.Add("Idempotency-Key", "api-error-idem");

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_error", body?.Code);
        Assert.Equal("api-error-trace", body?.TraceId);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed record HealthResponse(string Status, string TraceId);
}
