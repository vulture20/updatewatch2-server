using System.Text.RegularExpressions;

namespace UpdateWatch2.Server.Agents;

/// <summary>
/// Validates a hostname an agent self-reports at registration
/// (<c>POST /api/agents/{hostname}/register</c>, anonymous — an agent has
/// no client certificate yet at first contact) before it's ever persisted
/// as <see cref="Db.Entities.Agent.Hostname"/> or used to build a
/// certificate's Subject.
///
/// Found by a security review: <see cref="Certificates.InternalCertificateAuthority"/>
/// builds an issued leaf's Subject via naive string interpolation
/// (<c>$"CN={hostname}"</c>) into an <see cref="System.Security.Cryptography.X509Certificates.X500DistinguishedName"/>,
/// which parses its input per RFC 2253 — a hostname containing a comma or
/// equals sign (both legal, unencoded, in a URL path segment) is parsed as
/// additional RDNs rather than literal CN text, letting an anonymous,
/// unauthenticated caller inject arbitrary attribute/value pairs into a
/// certificate this server's own CA later vouches for. Rejecting anything
/// that isn't a plausible RFC 1123 hostname/FQDN here — the only place a
/// brand-new <see cref="Db.Entities.Agent"/> row's <c>Hostname</c> is ever
/// set — closes this off at the one point it matters, without needing to
/// touch <see cref="Certificates.InternalCertificateAuthority"/> itself
/// (which still shouldn't be trusted to receive DN-metacharacter-free
/// input from every future caller, but this is the concrete fix for the
/// concrete finding).
/// </summary>
public static class HostnameValidator
{
    // RFC 1123 hostname/FQDN: dot-separated labels, each 1-63 chars,
    // starting/ending alphanumeric, hyphens only in the middle. No comma,
    // '=', '/', whitespace, or other DN/path metacharacters can match this.
    private static readonly Regex Pattern = new(
        @"^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$",
        RegexOptions.Compiled);

    private const int MaxLength = 253;

    public static bool IsValid(string hostname) =>
        !string.IsNullOrEmpty(hostname) && hostname.Length <= MaxLength && Pattern.IsMatch(hostname);
}
