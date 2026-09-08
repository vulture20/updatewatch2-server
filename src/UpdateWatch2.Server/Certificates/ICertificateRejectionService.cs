using System.Security.Cryptography.X509Certificates;

namespace UpdateWatch2.Server.Certificates;

/// <summary>
/// Records and surfaces a rejected agent client certificate — high
/// priority/security-relevant per CLAUDE.md's "must be immediately visible
/// in the admin UI and reported, and logged" requirement. Called from
/// <c>Program.cs</c>'s certificate-authentication events, both for a
/// certificate that fails the auth middleware's own chain/validity-period
/// check (never reaches <see cref="ICertificateValidator"/> at all) and for
/// one that passes that check but doesn't match a known/approved agent
/// (<see cref="ICertificateValidator"/>'s own failure result).
/// </summary>
public interface ICertificateRejectionService
{
    /// <summary>
    /// Logs at Warning level (visible at this app's default log level, no
    /// need to switch to DEBUG to notice it) and writes an audit log entry.
    /// <paramref name="certificate"/> may be null in the rare case none was
    /// available to inspect — best-effort identifying details are included
    /// where possible, never required.
    /// </summary>
    Task RecordAsync(CertificateRejectionReason reason, X509Certificate2? certificate, string? remoteIpAddress, CancellationToken ct = default);

    /// <summary>Recent rejections (see <see cref="CertificateRejectionStatusDto"/>) — backs the admin UI's warning banner.</summary>
    Task<CertificateRejectionStatusDto> GetStatusAsync(CancellationToken ct = default);
}
