using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Contracts.Common;
using HospitalAi.Domain.Common;
using System.Text.Json;

namespace HospitalAi.Api;

/// <summary>
/// 将应用层异常统一转换为 ErrorResponse，避免接口返回不一致的错误结构。
/// 响应体序列化为 ApiEnvelope 形状 { code, message, data }，与成功响应一致，
/// 前端 api/http.ts 按 code 是否为 0 判定成功/业务错误。
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        IRequestContext requestContext)
    {
        try
        {
            await next(httpContext);
        }
        catch (ValidationException exception)
        {
            await WriteErrorAsync(
                httpContext,
                StatusCodes.Status400BadRequest,
                "validation_error",
                exception.Message,
                requestContext.TraceId);
        }
        catch (ResourceNotFoundException exception)
        {
            await WriteErrorAsync(
                httpContext,
                StatusCodes.Status404NotFound,
                "not_found",
                exception.Message,
                requestContext.TraceId);
        }
        catch (DomainException exception)
        {
            await WriteErrorAsync(
                httpContext,
                StatusCodes.Status400BadRequest,
                "domain_error",
                exception.Message,
                requestContext.TraceId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "请求处理失败，TraceId={TraceId}",
                requestContext.TraceId);
            await WriteErrorAsync(
                httpContext,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "服务器内部错误。",
                requestContext.TraceId);
        }
    }

    private static async Task WriteErrorAsync(
        HttpContext httpContext,
        int statusCode,
        string code,
        string message,
        string traceId)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        var response = new ErrorResponse(code, message, traceId, null);
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
