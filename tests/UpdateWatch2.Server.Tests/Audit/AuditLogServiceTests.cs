using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Tests.Audit;

public class AuditLogServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-audit-log-test-{Guid.NewGuid()}.sqlite");
    private readonly AppDbContext _db;
    private readonly AuditLogService _service;

    public AuditLogServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();
        _service = new AuditLogService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task GetPageAsync_returns_entries_newest_first()
    {
        await _service.LogAsync("admin", "agent.approve", "host-1");
        await _service.LogAsync("admin", "agent.delete", "host-2");

        var page = await _service.GetPageAsync(1, 50);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("agent.delete", page.Entries[0].Action);
        Assert.Equal("agent.approve", page.Entries[1].Action);
    }

    [Fact]
    public async Task GetPageAsync_paginates()
    {
        for (var i = 0; i < 5; i++)
        {
            await _service.LogAsync("admin", $"action.{i}");
        }

        var firstPage = await _service.GetPageAsync(1, 2);
        var secondPage = await _service.GetPageAsync(2, 2);

        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Entries.Count);
        Assert.Equal("action.4", firstPage.Entries[0].Action);
        Assert.Equal("action.3", firstPage.Entries[1].Action);
        Assert.Equal(2, secondPage.Entries.Count);
        Assert.Equal("action.2", secondPage.Entries[0].Action);
        Assert.Equal("action.1", secondPage.Entries[1].Action);
    }

    [Fact]
    public async Task GetPageAsync_clamps_page_and_pageSize_to_sane_bounds()
    {
        await _service.LogAsync("admin", "agent.approve");

        var page = await _service.GetPageAsync(page: 0, pageSize: 10000);

        Assert.Equal(1, page.Page);
        Assert.Equal(200, page.PageSize);
    }

    [Fact]
    public async Task GetPageAsync_filters_by_search_across_actor_action_and_details()
    {
        await _service.LogAsync("admin", "agent.approve", "host-1");
        await _service.LogAsync("alice", "agent.delete", "host-2");
        await _service.LogAsync("agent", "agent.certificate.issued", "host-3");

        var byActor = await _service.GetPageAsync(1, 50, search: "alice");
        var byAction = await _service.GetPageAsync(1, 50, search: "delete");
        var byDetails = await _service.GetPageAsync(1, 50, search: "host-3");

        Assert.Single(byActor.Entries);
        Assert.Equal("alice", byActor.Entries[0].Actor);
        Assert.Single(byAction.Entries);
        Assert.Equal("agent.delete", byAction.Entries[0].Action);
        Assert.Single(byDetails.Entries);
        Assert.Equal("host-3", byDetails.Entries[0].Details);
    }

    [Fact]
    public async Task GetPageAsync_search_is_case_insensitive()
    {
        await _service.LogAsync("Admin", "agent.approve", "Host-1");

        var page = await _service.GetPageAsync(1, 50, search: "host-1");

        Assert.Single(page.Entries);
    }

    [Fact]
    public async Task GetPageAsync_returns_nothing_when_search_matches_no_entry()
    {
        await _service.LogAsync("admin", "agent.approve");

        var page = await _service.GetPageAsync(1, 50, search: "no-such-thing");

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Entries);
    }

    [Fact]
    public async Task GetPageAsync_returns_an_empty_page_with_no_entries()
    {
        var page = await _service.GetPageAsync(1, 50);

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Entries);
    }

    [Fact]
    public async Task PurgeOlderThanAsync_deletes_only_entries_older_than_the_retention_window()
    {
        await AddBackdatedAsync("stale", TimeSpan.FromDays(100));
        await AddBackdatedAsync("fresh", TimeSpan.FromDays(10));

        var deleted = await _service.PurgeOlderThanAsync(retentionDays: 90);

        Assert.Equal(1, deleted);
        // "fresh" survives, plus the purge's own audit-log.purged entry it
        // just wrote (see the next test) — not just "fresh" alone.
        var remaining = await _service.GetPageAsync(1, 50);
        Assert.Equal(2, remaining.TotalCount);
        Assert.Contains(remaining.Entries, e => e.Action == "fresh");
        Assert.DoesNotContain(remaining.Entries, e => e.Action == "stale");
    }

    [Fact]
    public async Task PurgeOlderThanAsync_records_its_own_audit_entry_when_it_deletes_something()
    {
        await AddBackdatedAsync("stale", TimeSpan.FromDays(100));

        await _service.PurgeOlderThanAsync(retentionDays: 90);

        var page = await _service.GetPageAsync(1, 50);
        Assert.Contains(page.Entries, e => e.Actor == "system" && e.Action == "audit-log.purged");
    }

    [Fact]
    public async Task PurgeOlderThanAsync_does_not_log_anything_when_nothing_was_deleted()
    {
        await AddBackdatedAsync("fresh", TimeSpan.FromDays(1));

        var deleted = await _service.PurgeOlderThanAsync(retentionDays: 90);

        Assert.Equal(0, deleted);
        var page = await _service.GetPageAsync(1, 50);
        Assert.Equal(1, page.TotalCount); // just the one entry added above — no purge entry
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PurgeOlderThanAsync_is_a_no_op_when_retention_is_the_unlimited_sentinel_or_below(int retentionDays)
    {
        await AddBackdatedAsync("ancient", TimeSpan.FromDays(10000));

        var deleted = await _service.PurgeOlderThanAsync(retentionDays);

        Assert.Equal(0, deleted);
        var page = await _service.GetPageAsync(1, 50);
        Assert.Equal(1, page.TotalCount);
    }

    /// <summary>
    /// Writes an entry directly via the DbContext, not <see cref="AuditLogService.LogAsync"/>,
    /// specifically so its Timestamp can be backdated — LogAsync always
    /// stamps DateTimeOffset.UtcNow, with no way to control it from the
    /// outside.
    /// </summary>
    private async Task AddBackdatedAsync(string action, TimeSpan age)
    {
        _db.AuditLogEntries.Add(new AuditLogEntry
        {
            Actor = "test",
            Action = action,
            Timestamp = DateTimeOffset.UtcNow - age,
        });
        await _db.SaveChangesAsync();
    }
}
