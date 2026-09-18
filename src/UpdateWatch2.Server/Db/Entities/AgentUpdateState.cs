namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// The single, persisted row describing the newest agent release this
/// server currently knows about and has already downloaded to local
/// storage (updatewatch2-server#14) — one-row singleton table, the same
/// convention <see cref="AdminSettings"/> already uses. A null
/// <see cref="LatestVersion"/> means no release has ever been
/// successfully checked yet.
///
/// Six fixed asset slots (Windows installer / Debian package / RPM
/// package, each × x64/arm64 — updatewatch2-agent#22/#23) rather than a
/// child table of arbitrary assets — this project's own release pipeline
/// (agent repo's <c>release.yml</c>) always publishes exactly these six,
/// so a flat, singleton-row shape stays consistent with how the rest of
/// this settings-like data is modeled rather than introducing a
/// one-to-many relation for a fixed, small set of well-known kinds. Was
/// three slots (one per kind, no architecture dimension) until a direct
/// user question ("wurde beim Selfupdate berücksichtigt, dass es jetzt
/// zusätzliche Releases gibt?") surfaced that the three-slot shape
/// predated the second architecture per kind entirely — every release now
/// carries two files per kind, and the old shape had no way to keep both,
/// so whichever one <see cref="AgentUpdates.AgentUpdateService"/> happened
/// to process last for a given kind silently overwrote the other, leaving
/// roughly half of a fleet's agents (by architecture) offered the wrong
/// platform's binary to self-update to.
/// </summary>
public class AgentUpdateState
{
    public int Id { get; set; }

    public string? LatestVersion { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    public string? WindowsInstallerX64FileName { get; set; }

    public string? WindowsInstallerX64Sha256 { get; set; }

    public long? WindowsInstallerX64SizeBytes { get; set; }

    public string? WindowsInstallerArm64FileName { get; set; }

    public string? WindowsInstallerArm64Sha256 { get; set; }

    public long? WindowsInstallerArm64SizeBytes { get; set; }

    public string? LinuxDebX64FileName { get; set; }

    public string? LinuxDebX64Sha256 { get; set; }

    public long? LinuxDebX64SizeBytes { get; set; }

    public string? LinuxDebArm64FileName { get; set; }

    public string? LinuxDebArm64Sha256 { get; set; }

    public long? LinuxDebArm64SizeBytes { get; set; }

    public string? LinuxRpmX64FileName { get; set; }

    public string? LinuxRpmX64Sha256 { get; set; }

    public long? LinuxRpmX64SizeBytes { get; set; }

    public string? LinuxRpmArm64FileName { get; set; }

    public string? LinuxRpmArm64Sha256 { get; set; }

    public long? LinuxRpmArm64SizeBytes { get; set; }

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
