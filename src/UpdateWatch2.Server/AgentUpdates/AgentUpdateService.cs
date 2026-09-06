using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.AgentUpdates;

public class AgentUpdateService(
    AppDbContext db,
    IGitHubReleaseClient gitHubClient,
    IAdminSettingsStore settingsStore,
    IAuditLogService auditLog,
    AgentUpdateStorageOptions storage,
    ILogger<AgentUpdateService> logger) : IAgentUpdateService
{
    // Not admin-configurable — CLAUDE.md's "Agent auto-update" spec never
    // called for the repo itself to be editable, only whether checking
    // happens at all (Enabled) and how (GitHubToken).
    public const string Owner = "vulture20";
    public const string Repo = "updatewatch2-agent";

    public bool IsEnabled =>
        !string.Equals(Environment.GetEnvironmentVariable("UPDATEWATCH2_AUTOUPDATE"), "false", StringComparison.OrdinalIgnoreCase)
        && settingsStore.AgentAutoUpdate.Enabled;

    public async Task<AgentUpdateCheckOutcome> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return AgentUpdateCheckOutcome.Disabled;
        }

        var state = await GetOrCreateStateAsync(ct);

        GitHubRelease? release;
        try
        {
            release = await gitHubClient.GetLatestReleaseAsync(Owner, Repo, settingsStore.AgentAutoUpdate.GitHubToken, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            // InvalidOperationException: System.Net.Http.Json throws this
            // for a response body that isn't the JSON shape expected —
            // treated as a failure the same as a network error, not a bug,
            // since it can legitimately happen (GitHub API shape change,
            // a captive portal returning HTML instead of JSON, ...).
            logger.LogWarning(ex, "Failed to check GitHub for a new agent release.");
            return await RecordFailureAsync(state, ex.Message, ct);
        }

        state.CheckedAt = DateTimeOffset.UtcNow;

        if (release is null)
        {
            return await RecordFailureAsync(state, "No releases published yet.", ct);
        }

        var version = release.TagName.TrimStart('v', 'V');
        var isAlreadyKnownVersion = string.Equals(version, state.LatestVersion, StringComparison.Ordinal);
        if (isAlreadyKnownVersion && AssetsPresentOnDisk(state))
        {
            state.LastError = null;
            await db.SaveChangesAsync(ct);
            return AgentUpdateCheckOutcome.UpToDate;
        }

        if (isAlreadyKnownVersion)
        {
            // GitHub hasn't published anything new, but at least one file
            // this row claims to have already downloaded is gone from
            // AgentUpdates:Path — most likely that volume was lost or
            // recreated (see CLAUDE.md's note on it). Left alone, the row
            // would keep offering agents a download URL that 404s until
            // an actual new release eventually ships. Re-download the
            // same version's assets now instead of waiting for that.
            logger.LogWarning(
                "Agent release {Version} is already the newest known version, but one or more of its downloaded assets are missing from local storage — re-downloading.",
                version);
        }

        try
        {
            await DownloadAssetsAsync(state, release.Assets, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            logger.LogWarning(ex, "Found agent release {Version} but failed to download its assets.", version);
            return await RecordFailureAsync(state, ex.Message, ct);
        }

        state.LatestVersion = version;
        state.LastError = null;
        await db.SaveChangesAsync(ct);

        if (isAlreadyKnownVersion)
        {
            await auditLog.LogAsync("system", "agent-update.assets-redownloaded", version, ct);
            logger.LogInformation("Re-downloaded agent release {Version}'s assets after finding them missing from local storage.", version);
            return AgentUpdateCheckOutcome.Redownloaded;
        }

        await auditLog.LogAsync("system", "agent-update.detected", version, ct);
        logger.LogInformation("Downloaded new agent release {Version}.", version);
        return AgentUpdateCheckOutcome.Downloaded;
    }

    /// <summary>
    /// True iff every asset slot this state actually claims to have (a
    /// null filename means that release never carried that platform's
    /// package, not a problem) still has its file on disk. Checked on
    /// every version-unchanged tick, not just at download time, since the
    /// storage directory can be lost independently of the DB row that
    /// describes it (a separate Docker volume — see CLAUDE.md).
    /// </summary>
    private bool AssetsPresentOnDisk(AgentUpdateState state) =>
        IsPresentOrNotExpected(state.WindowsInstallerFileName)
        && IsPresentOrNotExpected(state.LinuxDebFileName)
        && IsPresentOrNotExpected(state.LinuxRpmFileName);

    private bool IsPresentOrNotExpected(string? fileName) =>
        fileName is null || File.Exists(Path.Combine(storage.Path, fileName));

    public async Task<AgentUpdateOffer?> GetOfferForAsync(string? currentAgentVersion, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var state = await db.AgentUpdateStates.SingleOrDefaultAsync(ct);
        if (state?.LatestVersion is null || IsUpToDate(currentAgentVersion, state.LatestVersion))
        {
            return null;
        }

        return new AgentUpdateOffer(
            state.LatestVersion,
            ToAssetOffer(state.WindowsInstallerFileName, state.WindowsInstallerSha256, state.WindowsInstallerSizeBytes),
            ToAssetOffer(state.LinuxDebFileName, state.LinuxDebSha256, state.LinuxDebSizeBytes),
            ToAssetOffer(state.LinuxRpmFileName, state.LinuxRpmSha256, state.LinuxRpmSizeBytes));
    }

    public async Task<AgentUpdateStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var state = await db.AgentUpdateStates.SingleOrDefaultAsync(ct);
        return new AgentUpdateStatusDto(IsEnabled, state?.LatestVersion, state?.CheckedAt, state?.LastError);
    }

    public async Task<string?> ResolveDownloadPathAsync(string fileName, CancellationToken ct = default)
    {
        var state = await db.AgentUpdateStates.SingleOrDefaultAsync(ct);
        if (state is null)
        {
            return null;
        }

        var isKnown = string.Equals(fileName, state.WindowsInstallerFileName, StringComparison.Ordinal)
            || string.Equals(fileName, state.LinuxDebFileName, StringComparison.Ordinal)
            || string.Equals(fileName, state.LinuxRpmFileName, StringComparison.Ordinal);
        if (!isKnown)
        {
            return null;
        }

        var path = Path.Combine(storage.Path, fileName);
        return File.Exists(path) ? path : null;
    }

    private async Task<AgentUpdateCheckOutcome> RecordFailureAsync(AgentUpdateState state, string error, CancellationToken ct)
    {
        state.LastError = error;
        await db.SaveChangesAsync(ct);
        return AgentUpdateCheckOutcome.Failed;
    }

    private async Task<AgentUpdateState> GetOrCreateStateAsync(CancellationToken ct)
    {
        var state = await db.AgentUpdateStates.SingleOrDefaultAsync(ct);
        if (state is not null)
        {
            return state;
        }

        state = new AgentUpdateState();
        db.AgentUpdateStates.Add(state);
        await db.SaveChangesAsync(ct);
        return state;
    }

    private async Task DownloadAssetsAsync(AgentUpdateState state, IReadOnlyList<GitHubReleaseAsset> assets, CancellationToken ct)
    {
        Directory.CreateDirectory(storage.Path);

        // Bounded disk usage: only the newest known version's assets are
        // ever kept around — a fresh admin-triggered/periodic check
        // replaces the previous set outright rather than accumulating one
        // release's worth of files forever.
        foreach (var existingFile in Directory.EnumerateFiles(storage.Path))
        {
            File.Delete(existingFile);
        }

        state.WindowsInstallerFileName = null;
        state.WindowsInstallerSha256 = null;
        state.WindowsInstallerSizeBytes = null;
        state.LinuxDebFileName = null;
        state.LinuxDebSha256 = null;
        state.LinuxDebSizeBytes = null;
        state.LinuxRpmFileName = null;
        state.LinuxRpmSha256 = null;
        state.LinuxRpmSizeBytes = null;

        foreach (var asset in assets)
        {
            if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var (sha256, size) = await DownloadOneAsync(asset, ct);
                state.WindowsInstallerFileName = asset.Name;
                state.WindowsInstallerSha256 = sha256;
                state.WindowsInstallerSizeBytes = size;
            }
            else if (asset.Name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
            {
                var (sha256, size) = await DownloadOneAsync(asset, ct);
                state.LinuxDebFileName = asset.Name;
                state.LinuxDebSha256 = sha256;
                state.LinuxDebSizeBytes = size;
            }
            else if (asset.Name.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase))
            {
                var (sha256, size) = await DownloadOneAsync(asset, ct);
                state.LinuxRpmFileName = asset.Name;
                state.LinuxRpmSha256 = sha256;
                state.LinuxRpmSizeBytes = size;
            }
            // Anything else (e.g. a checksums.txt an admin manually
            // attached) is deliberately ignored — only the three known
            // package kinds this project's own release pipeline publishes
            // are ever offered to an agent.
        }
    }

    private Task<(string Sha256, long SizeBytes)> DownloadOneAsync(GitHubReleaseAsset asset, CancellationToken ct) =>
        gitHubClient.DownloadAssetAsync(asset.BrowserDownloadUrl, Path.Combine(storage.Path, asset.Name), ct);

    private static AgentUpdateAssetOffer? ToAssetOffer(string? fileName, string? sha256, long? sizeBytes) =>
        fileName is null ? null : new AgentUpdateAssetOffer($"/api/agent/updates/{Uri.EscapeDataString(fileName)}", sha256!, sizeBytes!.Value);

    private static bool IsUpToDate(string? currentVersion, string latestVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return true;
        }

        // System.Version parses a bare "0.10.0"-style SemVer string fine
        // (three-part Major.Minor.Build) — this project's agent version
        // never uses a pre-release suffix, so no dedicated SemVer parser
        // is needed here.
        if (!Version.TryParse(currentVersion, out var current) || !Version.TryParse(latestVersion, out var latest))
        {
            return true;
        }

        return current >= latest;
    }
}
