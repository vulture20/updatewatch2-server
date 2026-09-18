using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Notifications;
using UpdateWatch2.Server.Schedules;
using UpdateWatch2.Server.UpdateFilters;

namespace UpdateWatch2.Server.Updates;

public class UpdateService(
    AppDbContext db,
    IAuditLogService auditLog,
    IAdminSettingsStore settingsStore,
    IEmailNotificationService email,
    ILogger<UpdateService> logger) : IUpdateService
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
        agent.LastUpdateCheckAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string MatchKey(string title, string? packageId) => packageId ?? title;

    /// <summary>
    /// Removes the just-installed <see cref="UpdateItem"/> rows the moment
    /// a Succeeded install-ack arrives, rather than relying solely on the
    /// agent's own immediate follow-up re-check-and-report call
    /// (<c>HeartbeatWorker.CheckAndReportNowAsync</c>, agent-side) to
    /// eventually notice they're gone — that call can fail (a transient
    /// network hiccup) or run before the OS-level update checker itself
    /// has caught up to the just-completed install, either of which would
    /// otherwise leave a just-installed update showing as still pending in
    /// the admin UI until the next scheduled check, possibly a long time
    /// later. This is the one place that already knows exactly what was
    /// requested (<see cref="Db.Entities.Agent.PendingInstallUpdateIds"/>,
    /// read here before it's cleared below) and can guarantee immediate,
    /// deterministic removal — a null value means "everything pending was
    /// requested", so every one of this agent's items is removed; a JSON
    /// array of PackageIds (the same agent-native identifiers
    /// <see cref="TriggerInstallAsync"/> already translated an admin's
    /// selection into) removes only those. Purely a fast, best-effort
    /// layer on top of — not a replacement for — the agent's own
    /// subsequent real report: if this removes something that (rarely)
    /// turns out to still genuinely be pending, that next report
    /// re-adds it, the same self-correcting pattern this codebase already
    /// relies on elsewhere (e.g. <c>certificateRotationPending</c>
    /// recomputing itself false on the next heartbeat).
    /// </summary>
    private async Task RemoveJustInstalledItemsAsync(Agent agent, CancellationToken ct)
    {
        var itemsQuery = db.UpdateItems.Where(u => u.AgentId == agent.Id);

        if (agent.PendingInstallUpdateIds is not null)
        {
            var packageIds = JsonSerializer.Deserialize<List<string>>(agent.PendingInstallUpdateIds) ?? [];
            itemsQuery = itemsQuery.Where(u => u.PackageId != null && packageIds.Contains(u.PackageId));
        }

        await itemsQuery.ExecuteDeleteAsync(ct);
    }

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

    public async Task<BulkInstallResult> TriggerInstallManyAsync(IReadOnlyList<string> hostnames, string triggeredBy, int? scheduleRunId = null, CancellationToken ct = default)
    {
        var agents = await db.Agents.Where(a => hostnames.Contains(a.Hostname)).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var agent in agents)
        {
            // Always "everything pending" — see BulkInstallRequest's doc
            // comment for why a cross-agent selection isn't offered.
            agent.PendingInstallUpdateIds = null;
            agent.PendingInstallRequestedAt = now;
            agent.PendingInstallScheduleRunId = scheduleRunId;
        }

        await db.SaveChangesAsync(ct);

        var triggeredHostnames = agents.Select(a => a.Hostname).ToHashSet();
        var notFound = hostnames.Where(h => !triggeredHostnames.Contains(h)).ToList();
        await auditLog.LogAsync(triggeredBy, "updates.install.trigger.bulk", string.Join(", ", triggeredHostnames), ct);

        return new BulkInstallResult(agents.Count, notFound);
    }

    public async Task<bool> AcknowledgeInstallAsync(string hostname, InstallOutcome outcome, string? errorDetail, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        if (outcome == InstallOutcome.Succeeded)
        {
            await RemoveJustInstalledItemsAsync(agent, ct);
        }

        // Additive (updatewatch2-server#25): if this pending install came
        // from a schedule run, record its outcome there too, before the
        // correlation is cleared below — a manually-triggered install
        // (PendingInstallScheduleRunId null) is untouched by this.
        if (agent.PendingInstallScheduleRunId is { } scheduleRunId)
        {
            var runAgent = await db.ScheduleRunAgents
                .Include(ra => ra.ScheduleRun!)
                .ThenInclude(r => r.Schedule)
                .SingleOrDefaultAsync(ra => ra.ScheduleRunId == scheduleRunId && ra.Hostname == hostname, ct);
            if (runAgent is not null)
            {
                runAgent.InstallStatus = outcome == InstallOutcome.Succeeded ? ScheduleRunActionStatus.Delivered : ScheduleRunActionStatus.Failed;
                runAgent.ErrorDetail = outcome == InstallOutcome.Failed ? errorDetail : null;

                if (outcome == InstallOutcome.Failed)
                {
                    var schedule = runAgent.ScheduleRun!.Schedule!;
                    await ScheduleFailureNotifier.NotifyAsync(
                        settingsStore, email, auditLog, logger,
                        schedule.Name, schedule.NotifyOnFailure,
                        subject: "UpdateWatch2: scheduled install failed",
                        body: $"The scheduled update install for agent \"{hostname}\" (schedule \"{schedule.Name}\") failed"
                            + (string.IsNullOrWhiteSpace(errorDetail) ? "." : $": {errorDetail}")
                            + "\n\nCheck the agent's detail page in the UpdateWatch2 admin UI for details.",
                        subjectDe: "UpdateWatch2: geplante Installation fehlgeschlagen",
                        bodyDe: $"Die geplante Update-Installation für Agent \"{hostname}\" (Zeitplan \"{schedule.Name}\") ist fehlgeschlagen"
                            + (string.IsNullOrWhiteSpace(errorDetail) ? "." : $": {errorDetail}")
                            + "\n\nDetails findest du auf der Agent-Detailseite in der UpdateWatch2-Verwaltungsoberfläche.",
                        auditAction: "schedule.run.install-failed.notified",
                        ct);
                }
            }
        }

        agent.PendingInstallRequestedAt = null;
        agent.PendingInstallUpdateIds = null;
        agent.PendingInstallScheduleRunId = null;
        agent.LastInstallOutcome = outcome.ToString();
        // Only ever meaningful for a Failed outcome — cleared on a
        // Succeeded ack rather than left stale from a previous failed
        // attempt, so AgentDetailPage never shows an old error detail
        // next to a since-successful install.
        agent.LastInstallErrorDetail = outcome == InstallOutcome.Failed ? errorDetail : null;
        agent.LastInstallCompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync("agent", $"updates.install.{outcome.ToString().ToLowerInvariant()}", hostname, ct);
        return true;
    }
}
