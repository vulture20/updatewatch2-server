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
/// how the agent's report of having acted on it clears that marker. The
/// actual poll/response wiring an agent sees is covered by
/// AgentRegistrationServiceTests (RecordAliveAsync) and
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

        var found = await _service.TriggerInstallAsync("install-host", triggeredBy: "admin");

        Assert.True(found);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "install-host");
        Assert.NotNull(agent.PendingInstallRequestedAt);
    }

    [Fact]
    public async Task TriggerInstallAsync_returns_false_for_an_unknown_agent()
    {
        var found = await _service.TriggerInstallAsync("no-such-host", triggeredBy: "admin");

        Assert.False(found);
    }

    [Fact]
    public async Task AcknowledgeInstallAsync_clears_the_pending_request_and_records_the_outcome()
    {
        var agent = new Agent { Hostname = "ack-host", Approved = true, PendingInstallRequestedAt = DateTimeOffset.UtcNow };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        var found = await _service.AcknowledgeInstallAsync("ack-host", InstallOutcome.Succeeded);

        Assert.True(found);
        var reloaded = await _db.Agents.SingleAsync(a => a.Hostname == "ack-host");
        Assert.Null(reloaded.PendingInstallRequestedAt);
        Assert.Equal("Succeeded", reloaded.LastInstallOutcome);
        Assert.NotNull(reloaded.LastInstallCompletedAt);
    }

    [Fact]
    public async Task AcknowledgeInstallAsync_clears_the_pending_request_even_on_a_failed_outcome()
    {
        // A failure must not leave the pending marker set forever, re-
        // delivering the same command on every heartbeat — an admin who
        // wants to retry triggers again explicitly (IUpdateService's own
        // doc comment); this is not an automatic-retry mechanism.
        var agent = new Agent { Hostname = "ack-fail-host", Approved = true, PendingInstallRequestedAt = DateTimeOffset.UtcNow };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        await _service.AcknowledgeInstallAsync("ack-fail-host", InstallOutcome.Failed);

        var reloaded = await _db.Agents.SingleAsync(a => a.Hostname == "ack-fail-host");
        Assert.Null(reloaded.PendingInstallRequestedAt);
        Assert.Equal("Failed", reloaded.LastInstallOutcome);
    }

    [Fact]
    public async Task AcknowledgeInstallAsync_returns_false_for_an_unknown_agent()
    {
        var found = await _service.AcknowledgeInstallAsync("no-such-host", InstallOutcome.Succeeded);

        Assert.False(found);
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
