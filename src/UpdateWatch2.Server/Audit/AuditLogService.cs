using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Audit;

public class AuditLogService(AppDbContext db) : IAuditLogService
{
    private const int MinPageSize = 1;
    private const int MaxPageSize = 200;

    public async Task LogAsync(string actor, string action, string? details = null, CancellationToken ct = default)
    {
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Actor = actor,
            Action = action,
            Details = details,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<AuditLogPageDto> GetPageAsync(int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, MinPageSize, MaxPageSize);

        var query = db.AuditLogEntries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // EF.Functions.Like, not .Contains — this exact project/EF Core
            // version has already been caught failing to translate a plain
            // string.StartsWith once (CertificateRejectionService), so Like
            // is used proactively here too rather than risking the same
            // class of gap with .Contains.
            var pattern = $"%{search}%";
            query = query.Where(e =>
                EF.Functions.Like(e.Actor, pattern) ||
                EF.Functions.Like(e.Action, pattern) ||
                (e.Details != null && EF.Functions.Like(e.Details, pattern)));
        }

        var totalCount = await query.CountAsync(ct);

        // Ordered by Id, not Timestamp: this project's EF Core/SQLite combo
        // can't translate an OrderBy on a DateTimeOffset column at all (see
        // CertificateRejectionService.GetStatusAsync's own comments) — Id
        // is auto-increment and every entry is written via LogAsync's own
        // default Timestamp, so ordering by Id descending gives the exact
        // same "newest first" order without touching that gap, and — unlike
        // that workaround's client-side materialize-then-sort — stays a
        // real server-side ORDER BY + LIMIT/OFFSET, so an unbounded audit
        // log table never has to be pulled into memory just to page it.
        var entries = await query
            .OrderByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditLogEntryDto(e.Id, e.Timestamp, e.Actor, e.Action, e.Details))
            .ToListAsync(ct);

        return new AuditLogPageDto(entries, totalCount, page, pageSize);
    }

    public async Task<int> PurgeOlderThanAsync(int retentionDays, CancellationToken ct = default)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(retentionDays);

        // Same EF-Core-on-SQLite "a DateTimeOffset comparison operator in a
        // LINQ predicate can't be translated" gap GetPageAsync's own
        // comments above and CertificateRejectionService.GetStatusAsync
        // already document — worked around differently here than either
        // of those: project down to just (Id, Timestamp), a lightweight
        // pair rather than full rows (Details included), filter for the
        // stale ones client-side, then bulk-delete by the resulting Id
        // list via ExecuteDeleteAsync — a real, single SQL DELETE, not a
        // load-entity-then-Remove-then-SaveChanges round trip. Filtering
        // by Id (an int) rather than Timestamp again for the delete itself
        // sidesteps the same translation gap a second time.
        var candidates = await db.AuditLogEntries
            .Select(e => new { e.Id, e.Timestamp })
            .ToListAsync(ct);
        var staleIds = candidates.Where(e => e.Timestamp < cutoff).Select(e => e.Id).ToList();

        if (staleIds.Count == 0)
        {
            return 0;
        }

        var deleted = await db.AuditLogEntries.Where(e => staleIds.Contains(e.Id)).ExecuteDeleteAsync(ct);
        if (deleted > 0)
        {
            await LogAsync("system", "audit-log.purged", $"{deleted} entr{(deleted == 1 ? "y" : "ies")} older than {retentionDays}d", ct);
        }

        return deleted;
    }
}
