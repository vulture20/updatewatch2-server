namespace UpdateWatch2.Server.AgentUpdates;

/// <summary>
/// Checks GitHub for newer agent releases, downloads them to local
/// storage, and offers them to agents over the existing heartbeat channel
/// (updatewatch2-server#14). See <see cref="AgentUpdateService"/> for the
/// implementation and the design decision pinned on that issue (the
/// server downloads and re-serves the assets itself; agents never talk
/// to GitHub directly).
/// </summary>
public interface IAgentUpdateService
{
    /// <summary>
    /// True iff both <c>UPDATEWATCH2_AUTOUPDATE</c> and the admin-UI
    /// toggle allow this feature to run at all. The env var is checked
    /// here, not just by <see cref="AgentUpdateCheckWorker"/> — an offer
    /// already downloaded before the env var was set must also stop being
    /// handed out, not just stop being refreshed.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Checks GitHub once, downloading a newer release's assets if one is
    /// found. Called on <see cref="AgentUpdateCheckWorker"/>'s own
    /// interval — safe to call more often (e.g. an admin-triggered manual
    /// check), since it's a no-op read (<see cref="AgentUpdateCheckOutcome.UpToDate"/>)
    /// whenever nothing has changed on GitHub.
    /// </summary>
    Task<AgentUpdateCheckOutcome> CheckForUpdatesAsync(CancellationToken ct = default);

    /// <summary>
    /// The offer to hand an agent currently reporting <paramref name="currentAgentVersion"/>
    /// on its heartbeat — null if disabled, no release has ever been
    /// downloaded, or the agent is already on the newest known version
    /// (including when <paramref name="currentAgentVersion"/> itself is
    /// null/unparseable, which errs toward not offering anything rather
    /// than repeatedly pushing an update to a build too old to even
    /// report its own version correctly).
    /// </summary>
    Task<AgentUpdateOffer?> GetOfferForAsync(string? currentAgentVersion, CancellationToken ct = default);

    Task<AgentUpdateStatusDto> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves <paramref name="fileName"/> to a full path on disk iff it
    /// exactly matches one of the currently-known release assets — never
    /// combines untrusted input into a filesystem path otherwise, so this
    /// can't be used to read an arbitrary file. Null if there's no match
    /// or the file is missing.
    /// </summary>
    Task<string?> ResolveDownloadPathAsync(string fileName, CancellationToken ct = default);
}
