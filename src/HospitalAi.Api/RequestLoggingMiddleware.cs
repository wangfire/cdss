namespace HospitalAi.Api;

/// <summary>
/// 记录安全的结构化请求日志，不读取请求体，避免患者姓名、病历正文和 Prompt 进入日志。
/// </summary>
public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        RequestContext requestContext)
    {
        using var scope = logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["RequestId"] = requestContext.RequestId,
                ["TraceId"] = requestContext.TraceId,
                ["HospitalId"] = requestContext.HospitalId == Guid.Empty
                    ? null
                    : requestContext.HospitalId,
                ["UserId"] = requestContext.UserId
            });

        var startedAt = DateTimeOffset.UtcNow;
        await next(httpContext);
        var elapsedMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

        logger.LogInformation(
            "HTTP request completed {Method} {Path} {StatusCode} in {ElapsedMilliseconds} ms TraceId={TraceId} HospitalId={HospitalId} UserId={UserId}",
            httpContext.Request.Method,
            httpContext.Request.Path.Value,
            httpContext.Response.StatusCode,
            elapsedMs,
            requestContext.TraceId,
            requestContext.HospitalId == Guid.Empty
                ? null
                : requestContext.HospitalId,
            requestContext.UserId);
    }
}
