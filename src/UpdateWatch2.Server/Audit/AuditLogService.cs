using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Audit;

public class AuditLogService(AppDbContext db) : IAuditLogService
{
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
}
