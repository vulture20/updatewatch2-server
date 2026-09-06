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
}
