using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HospitalAi.Api.Security;

/// <summary>
/// 直通认证处理器：复用 RequestContextMiddleware 已构建的 ClaimsPrincipal（JWT / Header 占位认证），
/// 使 AuthorizationMiddleware 与 RequireAuthorization 策略可正常工作，避免“未注册 IAuthenticationService 导致 500”。
/// </summary>
public sealed class RequestContextAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "RequestContext";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.User.Identity?.IsAuthenticated == true)
        {
            var ticket = new AuthenticationTicket(
                Context.User,
                Context.User.Identity.AuthenticationType ?? SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }
}
