using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HospitalAi.Infrastructure.Security;
using Microsoft.IdentityModel.Tokens;

namespace HospitalAi.Api;

/// <summary>
/// 提取并回写请求追踪头，确保应用服务始终能拿到稳定的请求上下文。
/// 支持两种认证主体来源：
/// 1. Bearer JWT 令牌（前端登录签发，含 sub=userId + hospitalId）—— 校验后回写 UserId/HospitalId。
/// 2. 旧版 X-User-Id / X-Hospital-Id 请求头占位认证 —— 保持向后兼容。
/// </summary>
public sealed class RequestContextMiddleware(
    RequestDelegate next,
    IJwtOptionsProvider jwtOptionsProvider)
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

        // 优先解析 Bearer JWT（前端登录令牌）：校验签名与有效期后，
        // 用令牌声明覆盖 UserId/HospitalId，构建已认证的 ClaimsPrincipal。
        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var token = authHeader["Bearer ".Length..];
                var claims = ValidateJwt(token);
                if (claims is not null)
                {
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
            }
            catch (SecurityTokenException)
            {
                // 令牌无效/过期：回退到旧的 Header 认证或匿名。
            }
            catch (ArgumentException)
            {
                // 畸形令牌（非法 Base64 / JSON）：同样按匿名处理，避免 500。
            }
        }

        httpContext.Response.Headers["X-Request-Id"] = requestContext.RequestId;
        httpContext.Response.Headers["X-Trace-Id"] = requestContext.TraceId;

        await next(httpContext);
    }

    /// <summary>
    /// 校验 Bearer JWT。返回 null 表示令牌无效，调用方据此回退到旧 Header 认证。
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
