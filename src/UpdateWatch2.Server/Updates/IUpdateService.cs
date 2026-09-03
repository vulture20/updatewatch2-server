namespace UpdateWatch2.Server.Updates;

public interface IUpdateService
{
    Task<IReadOnlyList<UpdateItemDto>?> GetForAgentAsync(string hostname, CancellationToken ct = default);

    /// <summary>
    /// Replaces the known pending updates for an agent with what it just
    /// reported, and updates its reboot-required flag. Returns false if no
    /// agent with that hostname exists.
    /// </summary>
    Task<bool> ReportUpdatesAsync(string hostname, ReportUpdatesRequest report, CancellationToken ct = default);

    /// <summary>
    /// Remote-triggers installation of an agent's pending updates (CLAUDE.md
    /// section 2.1). Delivery to the agent isn't wired up yet — this only
    /// records the request. Returns false if no agent with that hostname
    /// exists.
    /// </summary>
    Task<bool> TriggerInstallAsync(string hostname, string triggeredBy, CancellationToken ct = default);
}
