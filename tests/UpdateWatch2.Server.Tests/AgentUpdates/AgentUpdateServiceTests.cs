using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.AgentUpdates;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.AgentUpdates;

public class AgentUpdateServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-agent-update-test-{Guid.NewGuid()}.sqlite");
    private readonly string _storageDirectory = Path.Combine(Path.GetTempPath(), $"uw2-agent-update-storage-{Guid.NewGuid()}");
    private readonly AppDbContext _db;
    private readonly FakeGitHubReleaseClient _gitHub = new();
    private readonly FakeAdminSettingsStore _settingsStore = new();
    private readonly AgentUpdateService _service;

    private static readonly GitHubRelease SampleRelease = new(
        "v0.11.0",
        [
            new GitHubReleaseAsset("UpdateWatch2Agent-Setup-0.11.0-x64.exe", "https://github.com/example/releases/download/v0.11.0/UpdateWatch2Agent-Setup-0.11.0-x64.exe", 1000),
            new GitHubReleaseAsset("updatewatch2-agent_0.11.0_amd64.deb", "https://github.com/example/releases/download/v0.11.0/updatewatch2-agent_0.11.0_amd64.deb", 2000),
            new GitHubReleaseAsset("updatewatch2-agent-0.11.0-1.x86_64.rpm", "https://github.com/example/releases/download/v0.11.0/updatewatch2-agent-0.11.0-1.x86_64.rpm", 3000),
        ]);

    public AgentUpdateServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();

        _service = new AgentUpdateService(
            _db, _gitHub, _settingsStore, new AuditLogService(_db),
            new AgentUpdateStorageOptions(_storageDirectory), NullLogger<AgentUpdateService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        File.Delete(_dbPath);
        if (Directory.Exists(_storageDirectory))
        {
            Directory.Delete(_storageDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_is_a_no_op_when_disabled_via_the_admin_toggle()
    {
        _settingsStore.AgentAutoUpdate = new AgentAutoUpdateOptions { Enabled = false };
        _gitHub.Release = SampleRelease;

        var outcome = await _service.CheckForUpdatesAsync();

        Assert.Equal(AgentUpdateCheckOutcome.Disabled, outcome);
        Assert.Empty(await _db.AgentUpdateStates.ToListAsync());
    }

    [Fact]
    public void IsEnabled_is_false_when_UPDATEWATCH2_AUTOUPDATE_is_set_to_false_even_though_the_admin_toggle_is_on()
    {
        _settingsStore.AgentAutoUpdate = new AgentAutoUpdateOptions { Enabled = true };
        Environment.SetEnvironmentVariable("UPDATEWATCH2_AUTOUPDATE", "false");
        try
        {
            Assert.False(_service.IsEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UPDATEWATCH2_AUTOUPDATE", null);
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_downloads_and_persists_a_newly_found_release()
    {
        _gitHub.Release = SampleRelease;

        var outcome = await _service.CheckForUpdatesAsync();

        Assert.Equal(AgentUpdateCheckOutcome.Downloaded, outcome);

        var state = await _db.AgentUpdateStates.SingleAsync();
        Assert.Equal("0.11.0", state.LatestVersion);
        Assert.NotNull(state.CheckedAt);
        Assert.Null(state.LastError);
        Assert.Equal("UpdateWatch2Agent-Setup-0.11.0-x64.exe", state.WindowsInstallerFileName);
        Assert.Equal("updatewatch2-agent_0.11.0_amd64.deb", state.LinuxDebFileName);
        Assert.Equal("updatewatch2-agent-0.11.0-1.x86_64.rpm", state.LinuxRpmFileName);
        Assert.NotNull(state.WindowsInstallerSha256);

        Assert.True(File.Exists(Path.Combine(_storageDirectory, "UpdateWatch2Agent-Setup-0.11.0-x64.exe")));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ignores_a_release_asset_whose_name_contains_a_path_traversal_sequence()
    {
        // Security review finding: a malicious/compromised release on the
        // pinned upstream repo could name an asset so that combining it
        // with the storage directory escapes it entirely — the fix is to
        // reject (not just warn about) any asset name Path.GetFileName
        // would change, before it's ever downloaded or recorded.
        _gitHub.Release = SampleRelease with
        {
            Assets =
            [
                new GitHubReleaseAsset("../../../etc/systemd/system/evil.exe", "https://github.com/example/releases/download/v0.11.0/evil.exe", 1000),
                new GitHubReleaseAsset("updatewatch2-agent_0.11.0_amd64.deb", "https://github.com/example/releases/download/v0.11.0/updatewatch2-agent_0.11.0_amd64.deb", 2000),
            ],
        };

        var outcome = await _service.CheckForUpdatesAsync();

        Assert.Equal(AgentUpdateCheckOutcome.Downloaded, outcome);
        var state = await _db.AgentUpdateStates.SingleAsync();
        Assert.Null(state.WindowsInstallerFileName);
        Assert.Equal("updatewatch2-agent_0.11.0_amd64.deb", state.LinuxDebFileName);

        // The traversal target must never have been written anywhere,
        // including outside the storage directory.
        Assert.False(File.Exists(Path.Combine(_storageDirectory, "../../../etc/systemd/system/evil.exe")));
        Assert.False(File.Exists(Path.GetFullPath(Path.Combine(_storageDirectory, "../../../etc/systemd/system/evil.exe"))));
        Assert.Equal(["updatewatch2-agent_0.11.0_amd64.deb"], Directory.GetFiles(_storageDirectory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_strips_the_leading_v_from_the_git_tag()
    {
        _gitHub.Release = SampleRelease with { TagName = "v0.11.0" };

        await _service.CheckForUpdatesAsync();

        Assert.Equal("0.11.0", (await _db.AgentUpdateStates.SingleAsync()).LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_returns_UpToDate_without_re_downloading_when_the_version_is_unchanged()
    {
        _gitHub.Release = SampleRelease;
        await _service.CheckForUpdatesAsync();
        var firstCheckedAt = (await _db.AgentUpdateStates.SingleAsync()).CheckedAt;

        await Task.Delay(10);
        var outcome = await _service.CheckForUpdatesAsync();

        Assert.Equal(AgentUpdateCheckOutcome.UpToDate, outcome);
        var state = await _db.AgentUpdateStates.SingleAsync();
        Assert.True(state.CheckedAt > firstCheckedAt);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_redownloads_when_a_known_asset_is_missing_from_disk_even_though_the_version_is_unchanged()
    {
        _gitHub.Release = SampleRelease;
        await _service.CheckForUpdatesAsync();
        var installerPath = Path.Combine(_storageDirectory, "UpdateWatch2Agent-Setup-0.11.0-x64.exe");
        Assert.True(File.Exists(installerPath));

        // Simulate the AgentUpdates:Path volume losing a file independently
        // of the DB row that still claims it exists (see CLAUDE.md's note
        // on that volume).
        File.Delete(installerPath);

        var outcome = await _service.CheckForUpdatesAsync();

        Assert.Equal(AgentUpdateCheckOutcome.Redownloaded, outcome);
        Assert.True(File.Exists(installerPath));
        var state = await _db.AgentUpdateStates.SingleAsync();
        Assert.Equal("0.11.0", state.LatestVersion);
        Assert.Null(state.LastError);
        var auditEntry = await _db.AuditLogEntries.SingleAsync(e => e.Action == "agent-update.assets-redownloaded");
        Assert.Equal("0.11.0", auditEntry.Details);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_returns_UpToDate_without_touching_disk_when_all_known_assets_are_still_present()
    {
        _gitHub.Release = SampleRelease;
        await _service.CheckForUpdatesAsync();
        var installerPath = Path.Combine(_storageDirectory, "UpdateWatch2Agent-Setup-0.11.0-x64.exe");
        var writtenAt = File.GetLastWriteTimeUtc(installerPath);

        await Task.Delay(10);
        var outcome = await _service.CheckForUpdatesAsync();

        Assert.Equal(AgentUpdateCheckOutcome.UpToDate, outcome);
        // Same mtime proves this wasn't silently re-downloaded — only a
        // genuinely missing file should trigger that.
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(installerPath));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_does_not_latch_onto_a_release_whose_assets_have_not_finished_publishing_yet()
    {
        // GitHub's release object can report the new tag before every
        // asset-upload job in the release pipeline has actually attached
        // a file to it — this must not be treated the same as "this
        // release genuinely has no platform assets".
        _gitHub.Release = new GitHubRelease("v0.15.5", []);

        var outcome = await _service.CheckForUpdatesAsync();

        Assert.Equal(AgentUpdateCheckOutcome.Failed, outcome);
        var state = await _db.AgentUpdateStates.SingleAsync();
        Assert.Null(state.LatestVersion);
        Assert.Contains("0.15.5", state.LastError);

        // The very next check, once GitHub has finished publishing the
        // real assets, must still pick the release up rather than having
        // been permanently marked "already known".
        _gitHub.Release = SampleRelease with { TagName = "v0.15.5" };
        var secondOutcome = await _service.CheckForUpdatesAsync();

        Assert.Equal(AgentUpdateCheckOutcome.Downloaded, secondOutcome);
        Assert.Equal("0.15.5", (await _db.AgentUpdateStates.SingleAsync()).LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_records_the_error_and_reports_Failed_when_the_GitHub_call_throws()
    {
        _gitHub.ThrowOnGetLatestRelease = new HttpRequestException("simulated network failure");

        var outcome = await _service.CheckForUpdatesAsync();

        Assert.Equal(AgentUpdateCheckOutcome.Failed, outcome);
        var state = await _db.AgentUpdateStates.SingleAsync();
        Assert.Contains("simulated network failure", state.LastError);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_passes_the_configured_token_through_to_the_GitHub_client()
    {
        _settingsStore.AgentAutoUpdate = new AgentAutoUpdateOptions { Enabled = true, GitHubToken = "ghp_test123" };
        _gitHub.Release = SampleRelease;

        await _service.CheckForUpdatesAsync();

        Assert.Equal(["ghp_test123"], _gitHub.RequestedTokens);
    }

    [Fact]
    public async Task GetOfferForAsync_returns_null_when_the_agent_is_already_on_the_latest_version()
    {
        _gitHub.Release = SampleRelease;
        await _service.CheckForUpdatesAsync();

        Assert.Null(await _service.GetOfferForAsync("0.11.0"));
        Assert.Null(await _service.GetOfferForAsync("0.12.0"));
    }

    [Fact]
    public async Task GetOfferForAsync_returns_null_when_the_current_version_is_unknown()
    {
        _gitHub.Release = SampleRelease;
        await _service.CheckForUpdatesAsync();

        Assert.Null(await _service.GetOfferForAsync(null));
    }

    [Fact]
    public async Task GetOfferForAsync_offers_the_newer_release_with_server_hosted_download_urls()
    {
        _gitHub.Release = SampleRelease;
        await _service.CheckForUpdatesAsync();

        var offer = await _service.GetOfferForAsync("0.9.0");

        Assert.NotNull(offer);
        Assert.Equal("0.11.0", offer!.Version);
        Assert.Equal("/api/agent/updates/UpdateWatch2Agent-Setup-0.11.0-x64.exe", offer.WindowsInstaller!.DownloadUrl);
        Assert.Equal("/api/agent/updates/updatewatch2-agent_0.11.0_amd64.deb", offer.LinuxDeb!.DownloadUrl);
        Assert.Equal("/api/agent/updates/updatewatch2-agent-0.11.0-1.x86_64.rpm", offer.LinuxRpm!.DownloadUrl);
    }

    [Fact]
    public async Task GetOfferForAsync_returns_null_when_disabled_even_if_a_release_was_already_downloaded()
    {
        _gitHub.Release = SampleRelease;
        await _service.CheckForUpdatesAsync();

        _settingsStore.AgentAutoUpdate = new AgentAutoUpdateOptions { Enabled = false };

        Assert.Null(await _service.GetOfferForAsync("0.9.0"));
    }

    [Fact]
    public async Task ResolveDownloadPathAsync_returns_null_for_a_filename_that_is_not_a_known_asset()
    {
        _gitHub.Release = SampleRelease;
        await _service.CheckForUpdatesAsync();

        // The path-traversal case this guards against — never resolved
        // against storage, since it's never one of the recorded filenames.
        Assert.Null(await _service.ResolveDownloadPathAsync("../../../etc/passwd"));
        Assert.Null(await _service.ResolveDownloadPathAsync("unknown-file.exe"));
    }

    [Fact]
    public async Task ResolveDownloadPathAsync_returns_the_real_path_for_a_known_asset()
    {
        _gitHub.Release = SampleRelease;
        await _service.CheckForUpdatesAsync();

        var path = await _service.ResolveDownloadPathAsync("UpdateWatch2Agent-Setup-0.11.0-x64.exe");

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task UploadAssetsAsync_is_a_no_op_when_disabled_via_the_admin_toggle()
    {
        _settingsStore.AgentAutoUpdate = new AgentAutoUpdateOptions { Enabled = false };

        var outcome = await _service.UploadAssetsAsync("0.13.0", [MakeUpload("updatewatch2-agent_0.13.0_amd64.deb", "deb-bytes")]);

        Assert.Equal(AgentUpdateUploadOutcome.Disabled, outcome);
        Assert.Empty(await _db.AgentUpdateStates.ToListAsync());
    }

    [Fact]
    public async Task UploadAssetsAsync_saves_the_files_computes_their_sha256_and_marks_the_state_manually_uploaded()
    {
        var outcome = await _service.UploadAssetsAsync(
            "0.13.0",
            [
                MakeUpload("UpdateWatch2Agent-Setup-0.13.0-x64.exe", "exe-bytes"),
                MakeUpload("updatewatch2-agent_0.13.0_amd64.deb", "deb-bytes"),
            ]);

        Assert.Equal(AgentUpdateUploadOutcome.Uploaded, outcome);

        var state = await _db.AgentUpdateStates.SingleAsync();
        Assert.Equal("0.13.0", state.LatestVersion);
        Assert.True(state.ManuallyUploaded);
        Assert.Null(state.LastError);
        Assert.Equal("UpdateWatch2Agent-Setup-0.13.0-x64.exe", state.WindowsInstallerFileName);
        Assert.Equal("updatewatch2-agent_0.13.0_amd64.deb", state.LinuxDebFileName);
        Assert.Null(state.LinuxRpmFileName);
        Assert.Equal(Sha256Of("exe-bytes"), state.WindowsInstallerSha256);
        Assert.True(File.Exists(Path.Combine(_storageDirectory, "UpdateWatch2Agent-Setup-0.13.0-x64.exe")));
    }

    [Fact]
    public async Task UploadAssetsAsync_offers_the_uploaded_version_the_same_way_a_GitHub_download_would()
    {
        await _service.UploadAssetsAsync("0.13.0", [MakeUpload("updatewatch2-agent_0.13.0_amd64.deb", "deb-bytes")]);

        var offer = await _service.GetOfferForAsync("0.12.0");

        Assert.NotNull(offer);
        Assert.Equal("0.13.0", offer!.Version);
        Assert.Equal("/api/agent/updates/updatewatch2-agent_0.13.0_amd64.deb", offer.LinuxDeb!.DownloadUrl);
    }

    [Fact]
    public async Task UploadAssetsAsync_uploading_a_new_version_replaces_slots_not_present_in_this_upload()
    {
        await _service.UploadAssetsAsync(
            "0.13.0",
            [
                MakeUpload("UpdateWatch2Agent-Setup-0.13.0-x64.exe", "exe-v1"),
                MakeUpload("updatewatch2-agent_0.13.0_amd64.deb", "deb-v1"),
            ]);

        // A genuinely different version, uploading only the .deb this
        // time — the stale .exe from 0.13.0 must not linger and be mixed
        // into the 0.14.0 offer.
        await _service.UploadAssetsAsync("0.14.0", [MakeUpload("updatewatch2-agent_0.14.0_amd64.deb", "deb-v2")]);

        var state = await _db.AgentUpdateStates.SingleAsync();
        Assert.Equal("0.14.0", state.LatestVersion);
        Assert.Null(state.WindowsInstallerFileName);
        Assert.Equal("updatewatch2-agent_0.14.0_amd64.deb", state.LinuxDebFileName);
        Assert.False(File.Exists(Path.Combine(_storageDirectory, "UpdateWatch2Agent-Setup-0.13.0-x64.exe")));
        Assert.False(File.Exists(Path.Combine(_storageDirectory, "updatewatch2-agent_0.13.0_amd64.deb")));
    }

    [Fact]
    public async Task UploadAssetsAsync_uploading_the_same_version_again_only_replaces_the_slots_provided()
    {
        await _service.UploadAssetsAsync(
            "0.13.0",
            [
                MakeUpload("UpdateWatch2Agent-Setup-0.13.0-x64.exe", "exe-v1"),
                MakeUpload("updatewatch2-agent_0.13.0_amd64.deb", "deb-v1"),
            ]);

        // Same version, only re-uploading the .deb — the .exe from the
        // earlier call in this same version must be left untouched.
        await _service.UploadAssetsAsync("0.13.0", [MakeUpload("updatewatch2-agent_0.13.0_amd64.deb", "deb-v2")]);

        var state = await _db.AgentUpdateStates.SingleAsync();
        Assert.Equal("UpdateWatch2Agent-Setup-0.13.0-x64.exe", state.WindowsInstallerFileName);
        Assert.Equal(Sha256Of("deb-v2"), state.LinuxDebSha256);
        Assert.True(File.Exists(Path.Combine(_storageDirectory, "UpdateWatch2Agent-Setup-0.13.0-x64.exe")));
    }

    [Fact]
    public async Task UploadAssetsAsync_replacing_a_slot_with_a_differently_named_file_deletes_the_superseded_one()
    {
        await _service.UploadAssetsAsync("0.13.0", [MakeUpload("updatewatch2-agent_0.13.0_amd64.deb", "deb-v1")]);
        var firstPath = Path.Combine(_storageDirectory, "updatewatch2-agent_0.13.0_amd64.deb");
        Assert.True(File.Exists(firstPath));

        await _service.UploadAssetsAsync("0.13.0", [MakeUpload("updatewatch2-agent_0.13.0+fixed_amd64.deb", "deb-v2")]);

        Assert.False(File.Exists(firstPath));
        Assert.True(File.Exists(Path.Combine(_storageDirectory, "updatewatch2-agent_0.13.0+fixed_amd64.deb")));
    }

    private static UploadedAgentAsset MakeUpload(string fileName, string content) =>
        new(fileName, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)));

    private static string Sha256Of(string content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexStringLower(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content)));
    }
}
