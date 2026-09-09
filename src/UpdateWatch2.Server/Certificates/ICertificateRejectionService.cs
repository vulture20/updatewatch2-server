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

    /// <summary>
    /// Recent, unacknowledged rejections (see <see cref="CertificateRejectionStatusDto"/>)
    /// — backs the admin UI's warning banner. A rejection at or before the
    /// last <see cref="AcknowledgeAsync"/> call doesn't count, even if it's
    /// still within the 24h lookback window; a rejection after it does,
    /// immediately.
    /// </summary>
    Task<CertificateRejectionStatusDto> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Silences the warning banner for every rejection recorded so far —
    /// audit-logged (action <c>certificate-rejections.acknowledge</c>), a
    /// single shared row visible to every admin session, not a per-session
    /// dismiss. Does NOT affect <see cref="GetRecentByHostnameAsync"/> (the
    /// per-agent warning icon) — acknowledging means "an admin has seen
    /// this", not "the underlying problem is fixed"; only a later
    /// successful heartbeat clears that.
    /// </summary>
    Task AcknowledgeAsync(string acknowledgedBy, CancellationToken ct = default);

    /// <summary>
    /// The most recent rejection per claimed hostname within the same
    /// lookback window <see cref="GetStatusAsync"/> uses, keyed by hostname
    /// — backs <c>AgentService</c> flagging an affected agent in the
    /// overview list and showing the reason on its detail page. A
    /// rejection whose hostname couldn't be resolved (see
    /// <see cref="RecordAsync"/>) is keyed by the remote IP address or "unknown"
    /// instead, which simply never matches a real agent — harmless, not
    /// filtered out specially.
    /// </summary>
    Task<IReadOnlyDictionary<string, CertificateRejectionDto>> GetRecentByHostnameAsync(CancellationToken ct = default);
}
