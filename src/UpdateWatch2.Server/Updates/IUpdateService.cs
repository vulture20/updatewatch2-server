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
    /// The agent's acknowledgement that it acted on a pending install
    /// request — clears <see cref="Db.Entities.Agent.PendingInstallRequestedAt"/>
    /// regardless of <paramref name="outcome"/> (a failure doesn't retry
    /// automatically; an admin who wants to retry just triggers again,
    /// matching the existing fire-and-forget trigger semantics) and records
    /// the outcome/timestamp for the admin UI. Returns false if no agent
    /// with that hostname exists.
    /// </summary>
    Task<bool> AcknowledgeInstallAsync(string hostname, InstallOutcome outcome, CancellationToken ct = default);
}
