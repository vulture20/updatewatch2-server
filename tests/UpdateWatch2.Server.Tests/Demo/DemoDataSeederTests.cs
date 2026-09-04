using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Demo;

namespace UpdateWatch2.Server.Tests.Demo;

public class DemoDataSeederTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-demo-seeder-test-{Guid.NewGuid()}.sqlite");
    private readonly AppDbContext _db;

    public DemoDataSeederTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();
    }

    public void Dispose()
    {
        _db.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task EnsureSeededAsync_creates_demo_agents_with_pending_updates()
    {
        var seeder = new DemoDataSeeder(_db, NullLogger<DemoDataSeeder>.Instance);

        await seeder.EnsureSeededAsync();

        var demoAgents = await _db.Agents.Where(a => a.Hostname.StartsWith(DemoDataSeeder.HostnamePrefix)).ToListAsync();
        Assert.Equal(6, demoAgents.Count);
        Assert.Contains(demoAgents, a => !a.Approved); // shows the approval queue
        Assert.Contains(demoAgents, a => a.RebootRequired);
        Assert.Contains(demoAgents, a => a.LastAliveAt == null); // never checked in
        Assert.Contains(demoAgents, a => a.PendingUpdateCount > 0);

        var updateItems = await _db.UpdateItems
            .Where(u => demoAgents.Select(a => a.Id).Contains(u.AgentId))
            .ToListAsync();
        Assert.NotEmpty(updateItems);

        // PendingUpdateCount on each agent must actually match the number
        // of UpdateItem rows seeded for it — this is what the admin UI's
        // overview list and detail view both read.
        foreach (var agent in demoAgents)
        {
            var actualCount = updateItems.Count(u => u.AgentId == agent.Id);
            Assert.Equal(agent.PendingUpdateCount, actualCount);
        }
    }

    [Fact]
    public async Task EnsureSeededAsync_is_idempotent()
    {
        var seeder = new DemoDataSeeder(_db, NullLogger<DemoDataSeeder>.Instance);
        await seeder.EnsureSeededAsync();
        var agentCountAfterFirstCall = await _db.Agents.CountAsync();
        var updateCountAfterFirstCall = await _db.UpdateItems.CountAsync();

        await seeder.EnsureSeededAsync();

        Assert.Equal(agentCountAfterFirstCall, await _db.Agents.CountAsync());
        Assert.Equal(updateCountAfterFirstCall, await _db.UpdateItems.CountAsync());
    }

    [Fact]
    public async Task EnsureSeededAsync_still_seeds_when_a_real_non_demo_agent_already_exists()
    {
        _db.Agents.Add(new Agent { Hostname = "real-workstation-01", Approved = true });
        await _db.SaveChangesAsync();
        var seeder = new DemoDataSeeder(_db, NullLogger<DemoDataSeeder>.Instance);

        await seeder.EnsureSeededAsync();

        var demoAgents = await _db.Agents.Where(a => a.Hostname.StartsWith(DemoDataSeeder.HostnamePrefix)).ToListAsync();
        Assert.Equal(6, demoAgents.Count);
        Assert.True(await _db.Agents.AnyAsync(a => a.Hostname == "real-workstation-01"));
    }
}
