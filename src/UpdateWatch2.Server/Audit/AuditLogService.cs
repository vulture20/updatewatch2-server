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
}
