using System.Text.Json;
using HospitalAi.Contracts.Common;

namespace HospitalAi.Application.Tests;

public sealed class ErrorResponseTests
{
    [Fact]
    public void Serialize_错误响应使用四个camelCase字段()
    {
        var response = new ErrorResponse(
            "validation_error",
            "请求参数无效。",
            "trace-123",
            new { field = "name" });

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response));
        var root = document.RootElement;

        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal("validation_error", root.GetProperty("code").GetString());
        Assert.Equal("请求参数无效。", root.GetProperty("message").GetString());
        Assert.Equal("trace-123", root.GetProperty("traceId").GetString());
        Assert.Equal("name", root.GetProperty("details").GetProperty("field").GetString());
        Assert.False(root.TryGetProperty("Code", out _));
        Assert.False(root.TryGetProperty("TraceId", out _));
    }
}
