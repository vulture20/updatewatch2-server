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
        var details = FormatDetails(reason, certificate, thumbprint, remoteIpAddress);

        logger.LogWarning("Rejected agent client certificate ({Reason}): {Details}", reason, details);

        await auditLog.LogAsync(thumbprint ?? "unknown", ActionPrefix + reason, details, ct);
    }

    public async Task<CertificateRejectionStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - RecentWindow;
        // Three real EF-Core-on-SQLite translation gaps found by actually
        // running this query, not by reasoning about it — all throw at
        // query-compile time on this project's EF Core 10.0.11:
        // (1) a plain string.StartsWith call can't be translated — use
        //     EF.Functions.Like instead.
        // (2) a DateTimeOffset comparison operator (">=", "<", ...) in a
        //     LINQ predicate can't be translated either.
        // (3) neither can an OrderBy on a DateTimeOffset column
        //     ("SQLite does not support expressions of type
        //     'DateTimeOffset' in ORDER BY clauses").
        // No other query in this codebase filters or orders by a
        // DateTimeOffset column, so none of this had surfaced before.
        // Worked around by only pushing the (translatable) prefix filter
        // to SQL, then doing both the ordering and the recency-window
        // filter client-side on the — already narrowed to just this
        // action's own entries — result set.
        var candidates = await db.AuditLogEntries
            .Where(e => EF.Functions.Like(e.Action, ActionPrefix + "%"))
            .ToListAsync(ct);

        var recent = candidates.Where(e => e.Timestamp >= cutoff).OrderByDescending(e => e.Timestamp).ToList();

        return new CertificateRejectionStatusDto(
            recent.Count,
            recent.Take(MaxRecentReturned)
                .Select(e => new CertificateRejectionDto(e.Timestamp, e.Action[ActionPrefix.Length..], e.Details))
                .ToList());
    }

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
