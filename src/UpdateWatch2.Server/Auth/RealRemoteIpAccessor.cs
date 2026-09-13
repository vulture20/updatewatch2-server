using System.Net;

namespace UpdateWatch2.Server.Auth;

/// <summary>
/// Captures each request's actual TCP peer address before
/// <c>app.UseForwardedHeaders()</c> can overwrite
/// <see cref="HttpContext.Connection"/>'s <c>RemoteIpAddress</c> from a
/// client-suppliable <c>X-Forwarded-For</c> header.
///
/// Security review finding: <c>Program.cs</c>'s <c>ForwardedHeadersOptions</c>
/// deliberately clears <c>KnownProxies</c>/<c>KnownIPNetworks</c> so
/// <c>X-Forwarded-Proto</c> works out of the box behind any reverse proxy,
/// with no per-deployment "which IP is my proxy" configuration needed —
/// see that options block's own comment. But the same allow-list also
/// gates <c>X-Forwarded-For</c>, and .NET's <c>ForwardedHeadersMiddleware</c>
/// has no way to trust one of those two headers more than the other — so
/// clearing it to make Proto convenient also means ANY directly-connecting
/// client (not just a real, known proxy) can set
/// <c>Connection.RemoteIpAddress</c> to whatever it wants. That value was
/// then used, unmodified, to decide <c>UPDATEWATCH2_TRUSTEDIP</c>'s
/// brute-force-lockout exemption (<see cref="BruteForceLoginService"/>) —
/// letting an attacker spoof their way past it with a single extra header,
/// on any deployment that has that (documented, encouraged) feature
/// configured at all.
///
/// The fix is to stop using the forwarded-headers-processed
/// <c>RemoteIpAddress</c> for that one security decision: this class
/// stashes the pristine, un-spoofable TCP peer address (captured by a
/// middleware registered in <c>Program.cs</c> BEFORE
/// <c>UseForwardedHeaders()</c> runs) so <see cref="BruteForceLoginService"/>
/// can key its exemption off that instead. Deliberately narrow — request
/// logging/audit entries still use the normal, forwarded
/// <c>RemoteIpAddress</c> (more useful to an admin reading them, and
/// log-spoofing via a claimed IP isn't a security decision the way the
/// brute-force exemption is).
/// </summary>
public static class RealRemoteIpAccessor
{
    private const string ItemsKey = "UpdateWatch2.RealRemoteIp";

    /// <summary>Called once, early in the pipeline, before <c>UseForwardedHeaders()</c> — see <c>Program.cs</c>.</summary>
    public static void CaptureCurrent(HttpContext context) =>
        context.Items[ItemsKey] = context.Connection.RemoteIpAddress;

    /// <summary>
    /// The address captured by <see cref="CaptureCurrent"/> for this
    /// request — including a genuinely null capture (e.g. a transport
    /// that never populates <see cref="System.Net.Http.HttpMethod"/>-level
    /// connection info, such as the in-memory <c>TestServer</c>), which
    /// must NOT fall back to reading <see cref="HttpContext.Connection"/>'s
    /// CURRENT value here: by the time a controller calls this, that value
    /// has already been through <c>UseForwardedHeaders()</c> and reflects
    /// whatever the request itself claimed — exactly the spoofable value
    /// this class exists to avoid. Only falls back to it when the ITEMS
    /// key is missing entirely, meaning <see cref="CaptureCurrent"/> never
    /// ran at all (a hand-built <see cref="HttpContext"/> that skips the
    /// real pipeline, e.g. a unit test).
    /// </summary>
    public static IPAddress? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemsKey, out var value)
            ? value as IPAddress
            : context.Connection.RemoteIpAddress;
}
