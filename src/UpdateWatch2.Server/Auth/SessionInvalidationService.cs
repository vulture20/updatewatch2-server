using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Auth;

public class SessionInvalidationService(AppDbContext db) : ISessionInvalidationService
{
    public async Task InvalidateAsync(string username, CancellationToken ct = default)
    {
        var row = await db.SessionInvalidations.SingleOrDefaultAsync(s => s.Username == username, ct);
        if (row is null)
        {
            row = new SessionInvalidation { Username = username, InvalidatedBefore = DateTimeOffset.UtcNow };
            db.SessionInvalidations.Add(row);
        }
        else
        {
            row.InvalidatedBefore = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> IsValidAsync(string username, DateTimeOffset issuedAtUtc, CancellationToken ct = default)
    {
        var row = await db.SessionInvalidations.AsNoTracking().SingleOrDefaultAsync(s => s.Username == username, ct);
        // Strictly-after, not >=: a ticket issued in the exact same instant
        // as an invalidation (the closest a race gets, for a single-server
        // in-process app with no clock skew to worry about) is treated as
        // pre-dating it — safer to reject a borderline-timed ticket than to
        // let a stolen one slip through on a tie.
        return row is null || issuedAtUtc > row.InvalidatedBefore;
    }
}
