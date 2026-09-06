namespace UpdateWatch2.Server.AgentUpdates;

/// <summary>
/// One downloadable asset offered to an agent — <see cref="DownloadUrl"/>
/// always points at THIS server (<c>GET /api/agent/updates/{fileName}</c>,
/// mTLS-gated the same as every other agent-facing route), never at
/// GitHub directly. That's a deliberate design decision, not an
/// implementation detail: it keeps an agent's only outbound network
/// dependency the server it already trusts, so only the server itself
/// needs internet access for this feature — see updatewatch2-server#14's
/// pinned issue comment.
/// </summary>
public record AgentUpdateAssetOffer(string DownloadUrl, string Sha256, long SizeBytes);

/// <summary>
/// Surfaced to an agent on its <c>alive</c> heartbeat response
/// (<see cref="Agents.AliveRecordResult.UpdateAvailable"/>) once a newer
/// agent version than the one it self-reports is known and auto-update
/// is enabled. Each asset slot is independently nullable — a release
/// might not (yet) carry every platform's package, and an agent build
/// that doesn't know how to act on this field yet (updatewatch2-agent#14
/// is not implemented at the time this was added) simply ignores the
/// whole object.
/// </summary>
public record AgentUpdateOffer(
    string Version,
    AgentUpdateAssetOffer? WindowsInstaller,
    AgentUpdateAssetOffer? LinuxDeb,
    AgentUpdateAssetOffer? LinuxRpm);

/// <summary>Read-only status shown on the admin Settings page.</summary>
public record AgentUpdateStatusDto(bool Enabled, string? LatestVersion, DateTimeOffset? CheckedAt, string? LastError);

public enum AgentUpdateCheckOutcome
{
    /// <summary>Neither GitHub nor local storage were touched — disabled via the admin toggle or UPDATEWATCH2_AUTOUPDATE=false.</summary>
    Disabled,

    /// <summary>Checked GitHub; the already-known version is still the newest.</summary>
    UpToDate,

    /// <summary>Found and successfully downloaded a newer release.</summary>
    Downloaded,

    /// <summary>The GitHub API call or an asset download failed — see the persisted <see cref="AgentUpdateStatusDto.LastError"/>.</summary>
    Failed,
}
