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
/// might not (yet) carry every platform/architecture's package, and an
/// agent build that doesn't know how to act on this field yet
/// (updatewatch2-agent#14 is not implemented at the time this was added)
/// simply ignores the whole object.
///
/// <para>
/// Six slots, not three — one per (<see cref="AgentUpdateAssetKind"/>,
/// <see cref="AgentUpdateAssetArch"/>) combination, replacing the original
/// three-slot shape (protocol bumped accordingly) after updatewatch2-agent#22/#23
/// added a second architecture for every kind: the old shape had no way to
/// tell an x64 asset apart from an arm64 one of the same kind, so a server
/// new enough to know about both architectures but still using the old
/// shape could only ever offer one of them per kind, arbitrarily. A build
/// old enough to only know the three old field names simply won't find
/// them in a response using the new names (System.Text.Json's default
/// unknown-property tolerance) — self-update becomes a no-op for it until
/// upgraded, which is the correct, safe degradation, not silently offering
/// the wrong architecture.
/// </para>
/// </summary>
public record AgentUpdateOffer(
    string Version,
    AgentUpdateAssetOffer? WindowsInstallerX64,
    AgentUpdateAssetOffer? WindowsInstallerArm64,
    AgentUpdateAssetOffer? LinuxDebX64,
    AgentUpdateAssetOffer? LinuxDebArm64,
    AgentUpdateAssetOffer? LinuxRpmX64,
    AgentUpdateAssetOffer? LinuxRpmArm64);

/// <summary>Read-only status shown on the admin Settings page.</summary>
public record AgentUpdateStatusDto(bool Enabled, string? LatestVersion, DateTimeOffset? CheckedAt, string? LastError, bool ManuallyUploaded);

/// <summary>
/// One file an admin is manually uploading via <c>POST /api/admin/agent-update-status/upload</c>
/// (the offline/air-gapped alternative to <see cref="IAgentUpdateService.CheckForUpdatesAsync"/>'s
/// GitHub download — see CLAUDE.md's "Agent auto-update" bullet).
/// <see cref="FileName"/> is only ever used to classify which of the six
/// known kind/architecture combinations (<see cref="AgentUpdateAssetClassifier"/>)
/// this is and as the name it's saved/offered under — never trusted as a path (see
/// <see cref="IAgentUpdateService.UploadAssetsAsync"/>'s own doc comment).
/// <see cref="Content"/> is owned and disposed by the caller (the
/// controller), not by whatever consumes this record.
/// </summary>
public record UploadedAgentAsset(string FileName, Stream Content);

public enum AgentUpdateUploadOutcome
{
    /// <summary>Saved and recorded successfully — offered to agents from the very next heartbeat onward.</summary>
    Uploaded,

    /// <summary>Rejected — this feature's Enabled toggle (or UPDATEWATCH2_AUTOUPDATE=false) is currently off.</summary>
    Disabled,
}

public enum AgentUpdateCheckOutcome
{
    /// <summary>Neither GitHub nor local storage were touched — disabled via the admin toggle or UPDATEWATCH2_AUTOUPDATE=false.</summary>
    Disabled,

    /// <summary>Checked GitHub; the already-known version is still the newest.</summary>
    UpToDate,

    /// <summary>Found and successfully downloaded a newer release.</summary>
    Downloaded,

    /// <summary>
    /// GitHub still reports the already-known version, but one or more of
    /// its previously-downloaded assets were missing from local storage
    /// (e.g. the <c>AgentUpdates:Path</c> volume was lost/recreated) —
    /// re-downloaded the same version's assets rather than leaving a
    /// stale <see cref="Db.Entities.AgentUpdateState"/> row pointing at
    /// files that no longer exist.
    /// </summary>
    Redownloaded,

    /// <summary>The GitHub API call or an asset download failed — see the persisted <see cref="AgentUpdateStatusDto.LastError"/>.</summary>
    Failed,
}
