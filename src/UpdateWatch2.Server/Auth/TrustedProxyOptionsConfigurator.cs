using System.Net;
using Microsoft.AspNetCore.Builder;

namespace UpdateWatch2.Server.Auth;

/// <summary>
/// Parses <c>UPDATEWATCH2_TRUSTED_PROXY</c> — a comma-separated list of IPs
/// and/or CIDR ranges — into <see cref="ForwardedHeadersOptions.KnownProxies"/>/
/// <see cref="ForwardedHeadersOptions.KnownIPNetworks"/>, restoring ASP.NET
/// Core's normal reverse-proxy allow-list for admins who know their proxy's
/// stable IP. Off by default (this env var unset) — see the
/// <c>AddOptions&lt;ForwardedHeadersOptions&gt;()</c> block in
/// <c>Program.cs</c> for why the allow-list is otherwise deliberately
/// cleared (trusts any directly-connecting peer).
///
/// <see cref="ForwardedHeadersOptions"/> itself is <c>Microsoft.AspNetCore.Builder.ForwardedHeadersOptions</c>
/// in this ASP.NET Core version, not the older <c>Microsoft.AspNetCore.HttpOverrides</c>
/// namespace the type historically lived in (that name is still around as
/// a compile-time forward for source compat, which resolves fine from a
/// <c>Microsoft.NET.Sdk.Web</c> project like <c>Program.cs</c>'s, but not
/// reliably from this test-adjacent project's plain <c>Microsoft.NET.Sdk</c>
/// + explicit <c>FrameworkReference</c> — confirmed by hand after the old
/// namespace produced a bare CS0246 here despite building fine elsewhere,
/// which a runtime metadata scan traced to the type only having a real
/// <c>TypeDefinition</c> under <c>Microsoft.AspNetCore.Builder</c>).
/// Referencing the current namespace directly sidesteps that gap entirely.
///
/// A bare IP is routed to <c>KnownProxies</c>; an entry containing <c>/</c>
/// is routed to <c>KnownIPNetworks</c> via <see cref="IPNetwork.TryParse(string, out IPNetwork)"/>
/// (<c>System.Net.IPNetwork</c>, built into .NET 8+ — the type
/// <c>KnownIPNetworks</c> is itself typed with in this version, confirmed
/// by hand; the similarly-named, older ASP.NET Core-specific <c>IPNetwork</c>
/// type is unrelated and not what this property expects, despite
/// predating the BCL one). Unlike <see cref="TrustedIpMatcher"/>, which
/// predates .NET's built-in type entirely and hand-rolls CIDR matching for
/// a single value/range pair, this needs to populate a *list*, so the
/// built-in type's own parser is used directly rather than duplicating
/// that logic. Validates the whole value atomically: one malformed entry
/// rejects the entire list rather than silently dropping just that entry,
/// so a typo can't leave a partial, confusing allow-list in place — the
/// caller then leaves the options exactly as they were (this project's
/// default: cleared, trust any peer), which is the same behavior as never
/// having set the env var at all, never a new hole.
/// </summary>
public static class TrustedProxyOptionsConfigurator
{
    public static bool TryApply(string? value, ForwardedHeadersOptions options, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var entries = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0)
        {
            return true;
        }

        var proxies = new List<IPAddress>();
        var networks = new List<IPNetwork>();

        foreach (var entry in entries)
        {
            if (entry.Contains('/'))
            {
                if (!IPNetwork.TryParse(entry, out var network))
                {
                    error = $"'{entry}' is not a valid CIDR range.";
                    return false;
                }

                networks.Add(network);
            }
            else
            {
                if (!IPAddress.TryParse(entry, out var address))
                {
                    error = $"'{entry}' is not a valid IP address.";
                    return false;
                }

                proxies.Add(address);
            }
        }

        foreach (var proxy in proxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(network);
        }

        return true;
    }
}
