using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;

namespace UpdateWatch2.Server.Certificates;

public class CertificateRejectionService(AppDbContext db, IAuditLogService auditLog, ILogger<CertificateRejectionService> logger)
    : ICertificateRejectionService
{
    /// <summary>
    /// The reason is encoded as an <see cref="Db.Entities.AuditLogEntry.Action"/>
    /// suffix (e.g. "agent.certificate.rejected.Expired") rather than
    /// parsed back out of <see cref="Db.Entities.AuditLogEntry.Details"/> —
    /// Action is already this codebase's "machine-readable category" field
    /// (see its own doc comment), so this keeps <see cref="GetStatusAsync"/>
    /// exact instead of guessing at a string format only this class writes.
    /// </summary>
    public const string ActionPrefix = "agent.certificate.rejected.";

    // No separate acknowledge/dismiss state (same reasoning as
    // SmtpWarningBanner, which has none either) — an old rejection just
    // ages out of the window on its own once resolved/stopped recurring.
    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(24);

    // Same reasoning as AgentService.MaxAffectedHostnamesReturned — the
    // count below is always the true total even when this list is capped.
    private const int MaxRecentReturned = 20;

    public async Task RecordAsync(CertificateRejectionReason reason, X509Certificate2? certificate, string? remoteIpAddress, CancellationToken ct = default)
    {
        var thumbprint = certificate?.GetCertHashString(HashAlgorithmName.SHA256);
        // Every agent leaf this CA issues has Subject "CN=<hostname>"
        // (InternalCertificateAuthority.IssueAgentLeaf), so the claimed
        // hostname is recoverable even from an expired/untrusted
        // certificate — expiry/trust don't affect whether Subject parses.
        // Best-effort only: a forged certificate could claim any CN, but a
        // claim that doesn't match a real Agent.Hostname simply never
        // flags anything (see AgentService), so there's nothing unsafe
        // about trusting it provisionally here.
        var claimedHostname = certificate?.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        var actor = !string.IsNullOrEmpty(claimedHostname) ? claimedHostname : thumbprint ?? "unknown";
        var details = FormatDetails(reason, certificate, thumbprint, remoteIpAddress);

        logger.LogWarning("Rejected agent client certificate ({Reason}): {Details}", reason, details);

        await auditLog.LogAsync(actor, ActionPrefix + reason, details, ct);
    }

    public async Task<CertificateRejectionStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var recent = await LoadRecentAsync(ct);

        return new CertificateRejectionStatusDto(
            recent.Count,
            recent.Take(MaxRecentReturned)
                .Select(ToDto)
                .ToList());
    }

    public async Task<IReadOnlyDictionary<string, CertificateRejectionDto>> GetRecentByHostnameAsync(CancellationToken ct = default)
    {
        var recent = await LoadRecentAsync(ct);

        // Actor is the claimed hostname for a resolvable rejection (see
        // RecordAsync) — a thumbprint or "unknown" Actor simply never
        // matches a real Agent.Hostname, so it's harmless to key on Actor
        // unconditionally rather than filtering those out first.
        return recent
            .GroupBy(e => e.Actor)
            .ToDictionary(g => g.Key, g => ToDto(g.First())); // recent is already newest-first
    }

    /// <summary>
    /// Three real EF-Core-on-SQLite translation gaps found by actually
    /// running this query, not by reasoning about it — all throw at
    /// query-compile time on this project's EF Core 10.0.11:
    /// (1) a plain string.StartsWith call can't be translated — use
    ///     EF.Functions.Like instead.
    /// (2) a DateTimeOffset comparison operator (">=", "&lt;", ...) in a
    ///     LINQ predicate can't be translated either.
    /// (3) neither can an OrderBy on a DateTimeOffset column
    ///     ("SQLite does not support expressions of type
    ///     'DateTimeOffset' in ORDER BY clauses").
    /// No other query in this codebase filters or orders by a
    /// DateTimeOffset column, so none of this had surfaced before.
    /// Worked around by only pushing the (translatable) prefix filter to
    /// SQL, then doing both the ordering and the recency-window filter
    /// client-side on the — already narrowed to just this action's own
    /// entries — result set.
    /// </summary>
    private async Task<List<Db.Entities.AuditLogEntry>> LoadRecentAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - RecentWindow;
        var candidates = await db.AuditLogEntries
            .Where(e => EF.Functions.Like(e.Action, ActionPrefix + "%"))
            .ToListAsync(ct);

        return candidates.Where(e => e.Timestamp >= cutoff).OrderByDescending(e => e.Timestamp).ToList();
    }

    private static CertificateRejectionDto ToDto(Db.Entities.AuditLogEntry entry) =>
        new(entry.Timestamp, entry.Action[ActionPrefix.Length..], entry.Details);

    private static string FormatDetails(CertificateRejectionReason reason, X509Certificate2? certificate, string? thumbprint, string? remoteIpAddress)
    {
        var parts = new List<string> { reason.ToString() };
        if (certificate is not null)
        {
            parts.Add($"subject=\"{certificate.Subject}\"");
            parts.Add($"validity={certificate.NotBefore:u}..{certificate.NotAfter:u}");
        }

        if (thumbprint is not null)
        {
            parts.Add($"thumbprint={thumbprint}");
        }

        if (remoteIpAddress is not null)
        {
            parts.Add($"remoteIp={remoteIpAddress}");
        }

        return string.Join(' ', parts);
    }
}
