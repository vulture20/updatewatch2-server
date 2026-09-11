using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.UpdateFilters;

namespace UpdateWatch2.Server.Updates;

public class UpdateService(AppDbContext db, IAuditLogService auditLog) : IUpdateService
{
    public async Task<IReadOnlyList<UpdateItemDto>?> GetForAgentAsync(string hostname, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return null;
        }

        var items = await db.UpdateItems
            .Where(u => u.AgentId == agent.Id)
            .OrderBy(u => u.Title)
            .ToListAsync(ct);

        // Applied here, live against the current filter list, rather than
        // at report time — see UpdateFilterMatcher and Db.Entities.UpdateFilter's
        // doc comment on why editing a filter must take effect immediately.
        var filters = await db.UpdateFilters.ToListAsync(ct);

        return items
            .Where(u => !UpdateFilterMatcher.IsExcluded(u.Title, filters))
            .Select(u => new UpdateItemDto(u.Id, u.Title, u.PackageId, u.Description, u.DetectedAt, u.Installed))
            .ToList();
    }

    public async Task<bool> ReportUpdatesAsync(string hostname, ReportUpdatesRequest report, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        var existing = await db.UpdateItems.Where(u => u.AgentId == agent.Id).ToListAsync(ct);

        // Merged against the previous report rather than wiping and
        // recreating every row on every single report — found by a user
        // report that the "Detected at" column always showed today's
        // date: this used to unconditionally delete every existing row
        // and rebuild it from scratch here, even for an update nothing
        // had actually changed about, resetting DetectedAt every time.
        // Matched by PackageId when both sides have one (the stable,
        // tool-native identifier — a KB number on Windows, a bare package
        // name on Linux), falling back to Title otherwise (e.g. a Windows
        // update with no KB article at all).
        var existingByKey = existing.ToDictionary(u => MatchKey(u.Title, u.PackageId));
        var reportedKeys = report.Updates.Select(u => MatchKey(u.Title, u.PackageId)).ToHashSet();

        // No longer reported at all — installed elsewhere, superseded, or
        // no longer applicable. Nothing worth preserving.
        db.UpdateItems.RemoveRange(existing.Where(u => !reportedKeys.Contains(MatchKey(u.Title, u.PackageId))));

        foreach (var update in report.Updates)
        {
            if (existingByKey.TryGetValue(MatchKey(update.Title, update.PackageId), out var match))
            {
                // Still pending — refresh whatever could genuinely have
                // changed (e.g. a Linux Title/Description embeds the
                // target version, which can move between reports), but
                // leave DetectedAt exactly as it already was.
                match.Title = update.Title;
                match.PackageId = update.PackageId;
                match.Description = update.Description;
            }
            else
            {
                db.UpdateItems.Add(new UpdateItem
                {
                    AgentId = agent.Id,
                    Title = update.Title,
                    PackageId = update.PackageId,
                    Description = update.Description,
                });
            }
        }

        agent.PendingUpdateCount = report.Updates.Count;
        agent.RebootRequired = report.RebootRequired;
        agent.LastAliveAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string MatchKey(string title, string? packageId) => packageId ?? title;

    public async Task<bool> TriggerInstallAsync(string hostname, string triggeredBy, IReadOnlyList<int>? updateItemIds, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        string auditDetails;
        if (updateItemIds is null)
        {
            // No selection made — install everything currently pending,
            // the original (and still supported) behavior.
            agent.PendingInstallUpdateIds = null;
            auditDetails = hostname;
        }
        else
        {
            // Translate the admin-selected UpdateItem rows (server-only
            // primary keys — an agent has no idea what they are) into the
            // PackageIds the agent's own next search can actually match
            // against. An id belonging to a different agent, already
            // gone, or with no PackageId at all (can't be individually
            // named on the wire — see WuaUpdateSession's own doc comment
            // on how that's handled agent-side) is silently dropped
            // rather than failing the whole request.
            var packageIds = await db.UpdateItems
                .Where(u => u.AgentId == agent.Id && updateItemIds.Contains(u.Id) && u.PackageId != null)
                .Select(u => u.PackageId!)
                .ToListAsync(ct);
            agent.PendingInstallUpdateIds = JsonSerializer.Serialize(packageIds);
            auditDetails = $"{hostname} ({packageIds.Count} selected)";
        }

        // Delivery is the agent's own alive-heartbeat poll picking this up
        // (updatewatch2-server#10/updatewatch2-agent#4) — see
        // AgentRegistrationService.RecordAliveAsync and
        // AgentProtocolController.Alive.
        agent.PendingInstallRequestedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(triggeredBy, "updates.install.trigger", auditDetails, ct);
        return true;
    }

    public async Task<bool> AcknowledgeInstallAsync(string hostname, InstallOutcome outcome, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        agent.PendingInstallRequestedAt = null;
        agent.PendingInstallUpdateIds = null;
        agent.LastInstallOutcome = outcome.ToString();
        agent.LastInstallCompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync("agent", $"updates.install.{outcome.ToString().ToLowerInvariant()}", hostname, ct);
        return true;
    }
}
