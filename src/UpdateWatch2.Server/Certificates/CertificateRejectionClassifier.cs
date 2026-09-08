using System.Security.Cryptography.X509Certificates;

namespace UpdateWatch2.Server.Certificates;

/// <summary>
/// Pure classification of why a certificate that failed the auth
/// middleware's own chain/validity-period check was rejected — split out
/// from <see cref="CertificateRejectionService"/> so it's testable without
/// a DB or logger, the same testable-orchestrator/untestable-platform-code
/// split this codebase already uses elsewhere (e.g.
/// <c>WindowsUpdateChecker</c>/<c>WuaUpdateSession</c>).
/// </summary>
public static class CertificateRejectionClassifier
{
    /// <summary>
    /// <see cref="X509Certificate2.NotBefore"/>/<see cref="X509Certificate2.NotAfter"/>
    /// are local-time <c>DateTime</c>s, not UTC — converted explicitly here
    /// rather than compared against a local "now", so this gives the right
    /// answer regardless of the server's own time zone configuration.
    /// </summary>
    public static CertificateRejectionReason Classify(X509Certificate2? certificate)
    {
        if (certificate is null)
        {
            // No certificate to inspect (shouldn't normally happen — Kestrel
            // already captured one during the TLS handshake for this event
            // to fire at all — but defensive rather than throwing).
            return CertificateRejectionReason.NotTrusted;
        }

        var nowUtc = DateTime.UtcNow;
        if (nowUtc > certificate.NotAfter.ToUniversalTime())
        {
            return CertificateRejectionReason.Expired;
        }

        if (nowUtc < certificate.NotBefore.ToUniversalTime())
        {
            return CertificateRejectionReason.NotYetValid;
        }

        // Within its validity period but still failed the handler's own
        // chain build/validity check — doesn't chain to a currently
        // trusted internal CA root, or fails some other structural check
        // (e.g. wrong key usage).
        return CertificateRejectionReason.NotTrusted;
    }
}
