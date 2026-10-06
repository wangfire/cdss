using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HospitalAi.Contracts.Common;
using HospitalAi.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HospitalAi.Api;

/// <summary>
/// 提取并回写请求追踪头，确保应用服务始终能拿到稳定的请求上下文。
/// 认证主体来源：
/// 1. Bearer JWT 令牌（前端登录签发，含 sub=userId + hospitalId + 角色声明）——校验后构建已认证主体；
/// 2. X-User-Id / X-Hospital-Id 占位头——仅本地联调通道（Security:AllowHeaderIdentity，
///    默认仅 Testing 环境开启，生产必须显式关闭），防止伪造身份头绕过认证。
/// 携带 Bearer 令牌但校验失败时直接 401，不再静默降级为匿名（WP-B2-5）。
/// </summary>
public sealed class RequestContextMiddleware(
    RequestDelegate next,
    IJwtOptionsProvider jwtOptionsProvider,
    IConfiguration configuration)
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

        var hospitalHeader = GetOptionalHeader(httpContext, "X-Hospital-Id");
        if (Guid.TryParse(hospitalHeader, out var headerHospitalId))
        {
            requestContext.HospitalId = headerHospitalId;
        }

        // 优先解析 Bearer JWT（前端登录令牌）：校验签名与有效期后，
        // 用令牌声明覆盖 UserId/HospitalId，构建已认证的 ClaimsPrincipal。
        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            IEnumerable<Claim>? claims;
            try
            {
                claims = ValidateJwt(authHeader["Bearer ".Length..]);
            }
            catch (SecurityTokenException)
            {
                claims = null;
            }
            catch (ArgumentException)
            {
                // 畸形令牌（非法 Base64 / JSON）同样按无效令牌处理。
                claims = null;
            }

            if (claims is null)
            {
                // 令牌无效/过期：显式 401。静默降级会让过期会话伪装成匿名请求，掩盖权限语义。
                await WriteUnauthorizedAsync(httpContext, requestContext);
                return;
            }

            var claimList = claims.ToList();
            var subClaim = claimList.FirstOrDefault(c => c.Type == "sub");
            if (subClaim is not null)
            {
                requestContext.UserId = subClaim.Value;
            }

            var hospitalClaim = claimList.FirstOrDefault(c => c.Type == "hospitalId");
            if (hospitalClaim is not null
                && Guid.TryParse(hospitalClaim.Value, out var jwtHospitalId))
            {
                requestContext.HospitalId = jwtHospitalId;
            }

            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                claimList,
                "Jwt"));
        }
        else if (
            configuration.GetValue(
                "Security:AllowHeaderIdentity",
                defaultValue: httpContext.RequestServices
                    .GetRequiredService<IHostEnvironment>()
                    .IsEnvironment("Testing"))
            && GetOptionalHeader(httpContext, "X-User-Id") is { } headerUserId)
        {
            // 占位头认证：默认仅 Testing 环境开启；生产必须保持关闭。
            requestContext.UserId = headerUserId;
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, headerUserId)],
                authenticationType: "Header"));
        }

        httpContext.Response.Headers["X-Request-Id"] = requestContext.RequestId;
        httpContext.Response.Headers["X-Trace-Id"] = requestContext.TraceId;

        await next(httpContext);
    }

    /// <summary>
    /// 校验 Bearer JWT。返回 null 表示令牌无效。
    /// </summary>
    private IEnumerable<Claim>? ValidateJwt(string token)
    {
        var options = jwtOptionsProvider.Options;
        if (string.IsNullOrEmpty(options.Key))
        {
            return null;
        }

        var symmetricKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(options.Key));
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false,
        };

        var principal = handler.ValidateToken(
            token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudience = options.Audience,
                IssuerSigningKey = symmetricKey,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                ValidateLifetime = true,
                NameClaimType = "sub",
            },
            out var securityToken);

        if (principal is null)
        {
            return null;
        }

        return principal.Claims.Select(claim => new Claim(claim.Type, claim.Value)).ToList();
    }

    private static async Task WriteUnauthorizedAsync(
        HttpContext httpContext,
        RequestContext requestContext)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(
            new ErrorResponse("unauthorized", "令牌无效或已过期。", requestContext.TraceId, null)));
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
