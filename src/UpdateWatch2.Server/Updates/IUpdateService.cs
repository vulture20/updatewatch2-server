namespace UpdateWatch2.Server.Updates;

public interface IUpdateService
{
    Task<IReadOnlyList<UpdateItemDto>?> GetForAgentAsync(string hostname, CancellationToken ct = default);

    /// <summary>
    /// Merges an agent's just-reported updates into what's already known
    /// for it (an already-known update, matched by PackageId or, failing
    /// that, Title, keeps its original <see cref="Db.Entities.UpdateItem.DetectedAt"/>
    /// rather than it being reset to "now"), and updates its
    /// reboot-required flag. Returns false if no agent with that hostname
    /// exists.
    /// </summary>
    Task<bool> ReportUpdatesAsync(string hostname, ReportUpdatesRequest report, CancellationToken ct = default);

    /// <summary>
    /// Remote-triggers installation of an agent's updates (CLAUDE.md
    /// section 2.1). Sets <see cref="Db.Entities.Agent.PendingInstallRequestedAt"/>,
    /// which the agent picks up on its next alive heartbeat
    /// (updatewatch2-server#10/updatewatch2-agent#4) — this call itself is
    /// fire-and-forget from the admin's perspective, same as before; the
    /// actual delivery is polling, not a direct push. <paramref name="updateItemIds"/>
    /// — the <see cref="Db.Entities.UpdateItem.Id"/> values an admin
    /// selected in the UI — installs only those (an admin's way to
    /// install some updates while sparing others); null installs
    /// everything currently pending, the original behavior, still the
    /// right choice for a caller with no specific selection to make.
    /// Returns false if no agent with that hostname exists.
    /// </summary>
    Task<bool> TriggerInstallAsync(string hostname, string triggeredBy, IReadOnlyList<int>? updateItemIds, CancellationToken ct = default);

    /// <summary>
    /// Triggers installation of everything currently pending for several
    /// agents at once (bulk install from the overview list) — see
    /// <see cref="TriggerInstallAsync"/> for the single-agent delivery
    /// mechanism this reuses. Always installs everything pending per agent;
    /// there is no cross-agent equivalent of that method's <c>updateItemIds</c>
    /// selection (see <see cref="BulkInstallRequest"/>'s doc comment for why).
    /// <paramref name="scheduleRunId"/> is additive (updatewatch2-server#25):
    /// when set (a <see cref="Schedules.ScheduleWorker"/>-driven call, never
    /// an admin-facing one), it's recorded on
    /// <see cref="Db.Entities.Agent.PendingInstallScheduleRunId"/> alongside
    /// the trigger, which is what makes this specific pending request
    /// subject to that schedule run's deadline-expiry sweep rather than
    /// staying pending indefinitely like an ordinary manual trigger.
    /// </summary>
    Task<BulkInstallResult> TriggerInstallManyAsync(IReadOnlyList<string> hostnames, string triggeredBy, int? scheduleRunId = null, CancellationToken ct = default);

    /// <summary>
    /// The agent's acknowledgement that it acted on a pending install
    /// request — clears <see cref="Db.Entities.Agent.PendingInstallRequestedAt"/>
    /// regardless of <paramref name="outcome"/> (a failure doesn't retry
    /// automatically; an admin who wants to retry just triggers again,
    /// matching the existing fire-and-forget trigger semantics) and records
    /// the outcome/timestamp/<paramref name="errorDetail"/> for the admin
    /// UI. A Succeeded outcome also immediately removes the just-installed
    /// items from the pending-updates list itself, rather than waiting on
    /// the agent's own next report to notice they're gone — see
    /// <see cref="UpdateService"/>'s private <c>RemoveJustInstalledItemsAsync</c>.
    /// Returns false if no agent with that hostname exists.
    /// </summary>
    Task<bool> AcknowledgeInstallAsync(string hostname, InstallOutcome outcome, string? errorDetail, CancellationToken ct = default);
}
