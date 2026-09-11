namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// The single, persisted row describing the newest agent release this
/// server currently knows about and has already downloaded to local
/// storage (updatewatch2-server#14) — one-row singleton table, the same
/// convention <see cref="AdminSettings"/> already uses. A null
/// <see cref="LatestVersion"/> means no release has ever been
/// successfully checked yet.
///
/// Three fixed asset slots (Windows installer, Debian package, RPM
/// package) rather than a child table of arbitrary assets — this
/// project's own release pipeline (agent repo's <c>release.yml</c>)
/// always publishes exactly these three, so a flat, singleton-row shape
/// stays consistent with how the rest of this settings-like data is
/// modeled rather than introducing a one-to-many relation for a fixed,
/// small set of well-known kinds.
/// </summary>
public class AgentUpdateState
{
    public int Id { get; set; }

    public string? LatestVersion { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    public string? WindowsInstallerFileName { get; set; }

    public string? WindowsInstallerSha256 { get; set; }

    public long? WindowsInstallerSizeBytes { get; set; }

    public string? LinuxDebFileName { get; set; }

    public string? LinuxDebSha256 { get; set; }

    public long? LinuxDebSizeBytes { get; set; }

    public string? LinuxRpmFileName { get; set; }

    public string? LinuxRpmSha256 { get; set; }

    public long? LinuxRpmSizeBytes { get; set; }

    /// <summary>
    /// Set when the most recent check (GitHub API call or asset download)
    /// failed — network error, GitHub rate limit, no releases published
    /// yet. Cleared on the next successful check. Surfaced read-only to
    /// the admin UI so a persistent failure (e.g. a bad token) is visible
    /// rather than silently retried forever with no feedback.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// True iff <see cref="LatestVersion"/>'s assets were placed here by an
    /// admin manually uploading them (<c>AgentUpdateService.UploadAssetsAsync</c>)
    /// rather than downloaded from GitHub — the escape hatch for a server
    /// that deliberately has no internet access (CLAUDE.md's "Agent
    /// auto-update" bullet). Reset to false the moment a real GitHub
    /// download succeeds (<c>DownloadAssetsAsync</c>), so this always
    /// reflects where the currently-offered assets actually came from, not
    /// just how they first arrived. Display-only — never affects which
    /// asset is offered or how it's validated (the SHA-256 check is
    /// identical either way).
    /// </summary>
    public bool ManuallyUploaded { get; set; }
}
