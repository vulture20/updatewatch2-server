using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Updates;

namespace UpdateWatch2.Server.Tests.Updates;

/// <summary>
/// Covers the remote-install delivery mechanism (updatewatch2-server#10):
/// TriggerInstallAsync sets a pending marker rather than only audit-logging
/// (as it used to before delivery existed), and AcknowledgeInstallAsync is
/// how the agent's report of having acted on it clears that marker —
/// including, now, an admin's optional selection of specific updates to
/// install while sparing others. The actual poll/response wiring an agent
/// sees is covered by AgentRegistrationServiceTests (RecordAliveAsync) and
/// AgentProtocolControllerTests-equivalent endpoint tests, not here — this
/// is the service layer in isolation.
/// </summary>
public class UpdateServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-update-service-test-{Guid.NewGuid()}.sqlite");
    private readonly AppDbContext _db;
    private readonly UpdateService _service;

    public UpdateServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();
        _service = new UpdateService(_db, new AuditLogService(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task TriggerInstallAsync_sets_a_pending_install_request_for_a_known_agent()
    {
        _db.Agents.Add(new Agent { Hostname = "install-host", Approved = true });
        await _db.SaveChangesAsync();

        var found = await _service.TriggerInstallAsync("install-host", triggeredBy: "admin", updateItemIds: null);

        Assert.True(found);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "install-host");
        Assert.NotNull(agent.PendingInstallRequestedAt);
        Assert.Null(agent.PendingInstallUpdateIds);
    }

    [Fact]
    public async Task TriggerInstallAsync_returns_false_for_an_unknown_agent()
    {
        var found = await _service.TriggerInstallAsync("no-such-host", triggeredBy: "admin", updateItemIds: null);

        Assert.False(found);
    }

    [Fact]
    public async Task TriggerInstallAsync_with_a_selection_stores_the_selected_updates_PackageIds()
    {
        var agent = new Agent { Hostname = "selective-install-host", Approved = true };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();
        var keep = new UpdateItem { AgentId = agent.Id, Title = "Keep this one", PackageId = "KB1" };
        var spare = new UpdateItem { AgentId = agent.Id, Title = "Spare this one", PackageId = "KB2" };
        _db.UpdateItems.AddRange(keep, spare);
        await _db.SaveChangesAsync();

        var found = await _service.TriggerInstallAsync("selective-install-host", triggeredBy: "admin", updateItemIds: [keep.Id]);

        Assert.True(found);
        var reloaded = await _db.Agents.SingleAsync(a => a.Hostname == "selective-install-host");
        Assert.Equal("""["KB1"]""", reloaded.PendingInstallUpdateIds);
    }

    [Fact]
    public async Task TriggerInstallAsync_selection_ignores_ids_belonging_to_a_different_agent_and_ones_with_no_PackageId()
    {
        var agent = new Agent { Hostname = "own-updates-host", Approved = true };
        var otherAgent = new Agent { Hostname = "other-updates-host", Approved = true };
        _db.Agents.AddRange(agent, otherAgent);
        await _db.SaveChangesAsync();
        var mine = new UpdateItem { AgentId = agent.Id, Title = "Mine", PackageId = "KB1" };
        var noPackageId = new UpdateItem { AgentId = agent.Id, Title = "No KB article" };
        var someoneElses = new UpdateItem { AgentId = otherAgent.Id, Title = "Not mine", PackageId = "KB9" };
        _db.UpdateItems.AddRange(mine, noPackageId, someoneElses);
        await _db.SaveChangesAsync();

        await _service.TriggerInstallAsync("own-updates-host", triggeredBy: "admin", updateItemIds: [mine.Id, noPackageId.Id, someoneElses.Id]);

        var reloaded = await _db.Agents.SingleAsync(a => a.Hostname == "own-updates-host");
        Assert.Equal("""["KB1"]""", reloaded.PendingInstallUpdateIds);
    }

    [Fact]
    public async Task AcknowledgeInstallAsync_clears_the_selected_update_ids_too()
    {
        var agent = new Agent
        {
            Hostname = "ack-selection-host",
            Approved = true,
            PendingInstallRequestedAt = DateTimeOffset.UtcNow,
            PendingInstallUpdateIds = """["KB1"]""",
        };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        await _service.AcknowledgeInstallAsync("ack-selection-host", InstallOutcome.Succeeded, errorDetail: null);

        var reloaded = await _db.Agents.SingleAsync(a => a.Hostname == "ack-selection-host");
        Assert.Null(reloaded.PendingInstallUpdateIds);
    }

    [Fact]
    public async Task AcknowledgeInstallAsync_clears_the_pending_request_and_records_the_outcome()
    {
        var agent = new Agent { Hostname = "ack-host", Approved = true, PendingInstallRequestedAt = DateTimeOffset.UtcNow };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        var found = await _service.AcknowledgeInstallAsync("ack-host", InstallOutcome.Succeeded, errorDetail: null);

        Assert.True(found);
        var reloaded = await _db.Agents.SingleAsync(a => a.Hostname == "ack-host");
        Assert.Null(reloaded.PendingInstallRequestedAt);
        Assert.Equal("Succeeded", reloaded.LastInstallOutcome);
        Assert.NotNull(reloaded.LastInstallCompletedAt);
    }

    [Fact]
    public async Task AcknowledgeInstallAsync_clears_the_pending_request_and_records_the_error_detail_on_a_failed_outcome()
    {
        // A failure must not leave the pending marker set forever, re-
        // delivering the same command on every heartbeat — an admin who
        // wants to retry triggers again explicitly (IUpdateService's own
        // doc comment); this is not an automatic-retry mechanism.
        var agent = new Agent { Hostname = "ack-fail-host", Approved = true, PendingInstallRequestedAt = DateTimeOffset.UtcNow };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        await _service.AcknowledgeInstallAsync("ack-fail-host", InstallOutcome.Failed, "apt-get exited with code 100: E: There were unauthenticated packages...");

        var reloaded = await _db.Agents.SingleAsync(a => a.Hostname == "ack-fail-host");
        Assert.Null(reloaded.PendingInstallRequestedAt);
        Assert.Equal("Failed", reloaded.LastInstallOutcome);
        Assert.Equal("apt-get exited with code 100: E: There were unauthenticated packages...", reloaded.LastInstallErrorDetail);
    }

    [Fact]
    public async Task AcknowledgeInstallAsync_clears_a_stale_error_detail_on_a_subsequent_successful_outcome()
    {
        var agent = new Agent
        {
            Hostname = "ack-recovers-host",
            Approved = true,
            PendingInstallRequestedAt = DateTimeOffset.UtcNow,
            LastInstallOutcome = "Failed",
            LastInstallErrorDetail = "a previous failure's detail",
        };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        await _service.AcknowledgeInstallAsync("ack-recovers-host", InstallOutcome.Succeeded, errorDetail: null);

        var reloaded = await _db.Agents.SingleAsync(a => a.Hostname == "ack-recovers-host");
        Assert.Equal("Succeeded", reloaded.LastInstallOutcome);
        Assert.Null(reloaded.LastInstallErrorDetail);
    }

    [Fact]
    public async Task AcknowledgeInstallAsync_returns_false_for_an_unknown_agent()
    {
        var found = await _service.AcknowledgeInstallAsync("no-such-host", InstallOutcome.Succeeded, errorDetail: null);

        Assert.False(found);
    }

    [Fact]
    public async Task ReportUpdatesAsync_preserves_DetectedAt_for_a_still_pending_update()
    {
        // The bug this guards against: a user report that the "Detected
        // at" column always showed today's date — ReportUpdatesAsync used
        // to unconditionally delete and recreate every UpdateItem row on
        // every single report, resetting DetectedAt every time even for
        // an update that had been pending for weeks.
        var agent = new Agent { Hostname = "detected-at-host", Approved = true };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();
        await _service.ReportUpdatesAsync("detected-at-host", new ReportUpdatesRequest(
            [new ReportedUpdate("Security Update", "KB123", "first description")], RebootRequired: false));
        var firstReportItem = await _db.UpdateItems.SingleAsync(u => u.AgentId == agent.Id);
        var originalDetectedAt = DateTimeOffset.UtcNow.AddDays(-10);
        firstReportItem.DetectedAt = originalDetectedAt;
        await _db.SaveChangesAsync();

        // Same PackageId, reported again — title/description could
        // plausibly differ (e.g. a Linux package's target version moved),
        // but this is still fundamentally the same pending update.
        await _service.ReportUpdatesAsync("detected-at-host", new ReportUpdatesRequest(
            [new ReportedUpdate("Security Update", "KB123", "second description")], RebootRequired: false));

        var reloaded = await _db.UpdateItems.SingleAsync(u => u.AgentId == agent.Id);
        Assert.Equal(originalDetectedAt, reloaded.DetectedAt);
        Assert.Equal("second description", reloaded.Description);
    }

    [Fact]
    public async Task ReportUpdatesAsync_matches_by_title_when_PackageId_is_null()
    {
        var agent = new Agent { Hostname = "no-packageid-host", Approved = true };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();
        await _service.ReportUpdatesAsync("no-packageid-host", new ReportUpdatesRequest(
            [new ReportedUpdate("Feature Update", null, "d1")], RebootRequired: false));
        var item = await _db.UpdateItems.SingleAsync(u => u.AgentId == agent.Id);
        var originalDetectedAt = DateTimeOffset.UtcNow.AddDays(-5);
        item.DetectedAt = originalDetectedAt;
        await _db.SaveChangesAsync();

        await _service.ReportUpdatesAsync("no-packageid-host", new ReportUpdatesRequest(
            [new ReportedUpdate("Feature Update", null, "d2")], RebootRequired: false));

        var reloaded = await _db.UpdateItems.SingleAsync(u => u.AgentId == agent.Id);
        Assert.Equal(originalDetectedAt, reloaded.DetectedAt);
    }

    [Fact]
    public async Task ReportUpdatesAsync_removes_updates_no_longer_reported_and_adds_a_fresh_DetectedAt_for_new_ones()
    {
        var agent = new Agent { Hostname = "diff-report-host", Approved = true };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();
        await _service.ReportUpdatesAsync("diff-report-host", new ReportUpdatesRequest(
            [new ReportedUpdate("Old Update", "KB1", null)], RebootRequired: false));

        var before = DateTimeOffset.UtcNow;
        await _service.ReportUpdatesAsync("diff-report-host", new ReportUpdatesRequest(
            [new ReportedUpdate("New Update", "KB2", null)], RebootRequired: false));

        var items = await _db.UpdateItems.Where(u => u.AgentId == agent.Id).ToListAsync();
        var item = Assert.Single(items);
        Assert.Equal("KB2", item.PackageId);
        Assert.True(item.DetectedAt >= before);
    }

    [Fact]
    public async Task GetForAgentAsync_excludes_updates_matching_an_active_filter()
    {
        var agent = new Agent { Hostname = "filter-list-host", Approved = true };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();
        _db.UpdateItems.AddRange(
            new UpdateItem { AgentId = agent.Id, Title = "Security Intelligence-Update für Microsoft Defender Antivirus" },
            new UpdateItem { AgentId = agent.Id, Title = "2026-08 Kumulatives Update für Windows 11" });
        _db.UpdateFilters.Add(new UpdateFilter { Name = "Defender", Pattern = "Security Intelligence-Update" });
        await _db.SaveChangesAsync();

        var items = await _service.GetForAgentAsync("filter-list-host");

        var item = Assert.Single(items!);
        Assert.Equal("2026-08 Kumulatives Update für Windows 11", item.Title);
    }

    [Fact]
    public async Task GetForAgentAsync_returns_everything_when_no_filter_matches()
    {
        var agent = new Agent { Hostname = "unfiltered-host", Approved = true };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();
        _db.UpdateItems.Add(new UpdateItem { AgentId = agent.Id, Title = "openssl Sicherheitsaktualisierung" });
        _db.UpdateFilters.Add(new UpdateFilter { Name = "Defender", Pattern = "Security Intelligence-Update" });
        await _db.SaveChangesAsync();

        var items = await _service.GetForAgentAsync("unfiltered-host");

        Assert.Single(items!);
    }
}
