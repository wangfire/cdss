using System.Net;

namespace HospitalAi.Api.Security;

/// <summary>
/// 登录限流的客户端分区键计算。
///
/// 背景：ForwardedHeadersMiddleware 会用 X-Forwarded-For 覆盖 Connection.RemoteIpAddress，
/// 而那个头完全由客户端控制 —— 每换一个值就换一个限流分区，固定窗口限流形同虚设。
/// 实测（.NET 10 / TestServer）：即便把 KnownIPNetworks 与 KnownProxies 都清空，
/// 中间件仍会套用客户端伪造的头。因此限流分区键不能直接读 Connection.RemoteIpAddress。
///
/// 做法：
///   1. 在转发头中间件「之前」把 TCP 对端地址存进 HttpContext.Items
///      （PeerConnection 层的来源，客户端无法通过 HTTP 头伪造）；
///   2. 只有当对端确实在可信代理白名单里时，才采信它带来的 X-Forwarded-For
///      （那是可信代理转发的真实客户端 IP）；
///   3. 其余情况一律用对端 IP；
///   4. 对端也拿不到时回退到固定串 —— 全站共享一个配额，
///      宁可共享也不可被绕过。
/// </summary>
public static class LoginRateLimitClientKey
{
    /// <summary>HttpContext.Items 里存放「转发头中间件处理前的 TCP 对端 IP」的键。</summary>
    public const string PeerIpItemKey = "cdss.peer-ip";

    private static HashSet<string> _trustedProxies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>可信反向代理白名单（IP 字面量）。</summary>
    public static IReadOnlyCollection<string> TrustedProxies => _trustedProxies;

    /// <summary>设置可信代理白名单（启动时调用一次）。</summary>
    public static void ConfigureTrustedProxies(IEnumerable<string> proxies)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proxy in proxies)
        {
            if (IPAddress.TryParse(proxy, out var parsed))
            {
                set.Add(parsed.ToString());
            }
        }

        _trustedProxies = set;
    }

    /// <summary>记录 TCP 对端地址。必须排在 UseForwardedHeaders 之前调用。</summary>
    public static void CapturePeerIp(HttpContext context)
    {
        var peer = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(peer))
        {
            context.Items[PeerIpItemKey] = peer;
        }
    }

    /// <summary>计算限流分区键。</summary>
    public static string Resolve(HttpContext context)
    {
        var peer = context.Items.TryGetValue(PeerIpItemKey, out var stored)
            ? stored as string
            : null;

        if (string.IsNullOrEmpty(peer))
        {
            // 兜底：正常情况下采集中间件一定先于本方法执行。
            return "unknown-client";
        }

        // 对端是可信代理时，它带来的 X-Forwarded-For 才是真实客户端 IP。
        if (_trustedProxies.Contains(peer))
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(first))
                {
                    return first;
                }
            }
        }

        return peer;
    }
}
