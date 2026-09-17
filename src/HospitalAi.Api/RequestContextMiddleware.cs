using System.Security.Claims;

namespace HospitalAi.Api;

/// <summary>
/// 提取并回写请求追踪头，确保应用服务始终能拿到稳定的请求上下文。
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        RequestContext requestContext)
    {
        requestContext.RequestId = GetOrCreateHeader(
            httpContext,
            "X-Request-Id");
        requestContext.TraceId = GetOrCreateHeader(
            httpContext,
            "X-Trace-Id");
        requestContext.IdempotencyKey = GetOptionalHeader(
            httpContext,
            "Idempotency-Key");
        requestContext.UserId = GetOptionalHeader(
            httpContext,
            "X-User-Id");
        if (!string.IsNullOrWhiteSpace(requestContext.UserId))
        {
            // 第一阶段只建立认证主体，完整角色矩阵等医院权限方案确认后再收敛。
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, requestContext.UserId)],
                authenticationType: "Header"));
        }

        var hospitalHeader = GetOptionalHeader(httpContext, "X-Hospital-Id");
        if (Guid.TryParse(hospitalHeader, out var hospitalId))
        {
            requestContext.HospitalId = hospitalId;
        }

        httpContext.Response.Headers["X-Request-Id"] = requestContext.RequestId;
        httpContext.Response.Headers["X-Trace-Id"] = requestContext.TraceId;

        await next(httpContext);
    }

    private static string GetOrCreateHeader(
        HttpContext httpContext,
        string headerName)
    {
        var value = GetOptionalHeader(httpContext, headerName);
        return string.IsNullOrWhiteSpace(value)
            ? Guid.NewGuid().ToString("N")
            : value;
    }

    private static string? GetOptionalHeader(
        HttpContext httpContext,
        string headerName)
    {
        return httpContext.Request.Headers.TryGetValue(headerName, out var value)
            ? value.ToString()
            : null;
    }
}
