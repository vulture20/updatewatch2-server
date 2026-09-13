using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Db;

namespace UpdateWatch2.Server.Tests.Auth;

public class SessionInvalidationServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-session-invalidation-test-{Guid.NewGuid()}.sqlite");
    private readonly AppDbContext _db;
    private readonly SessionInvalidationService _service;

    public SessionInvalidationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();
        _service = new SessionInvalidationService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task IsValidAsync_returns_true_for_a_username_that_has_never_been_invalidated()
    {
        Assert.True(await _service.IsValidAsync("admin", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task IsValidAsync_returns_false_for_a_ticket_issued_before_the_invalidation()
    {
        var issuedAt = DateTimeOffset.UtcNow;
        await Task.Delay(10);
        await _service.InvalidateAsync("admin");

        Assert.False(await _service.IsValidAsync("admin", issuedAt));
    }

    [Fact]
    public async Task IsValidAsync_returns_true_for_a_ticket_issued_after_the_invalidation()
    {
        await _service.InvalidateAsync("admin");
        await Task.Delay(10);
        var issuedAt = DateTimeOffset.UtcNow;

        Assert.True(await _service.IsValidAsync("admin", issuedAt));
    }

    [Fact]
    public async Task InvalidateAsync_only_affects_the_named_username()
    {
        var issuedAt = DateTimeOffset.UtcNow;
        await Task.Delay(10);
        await _service.InvalidateAsync("admin");

        Assert.True(await _service.IsValidAsync("someone-else", issuedAt));
    }

    [Fact]
    public async Task InvalidateAsync_can_be_called_repeatedly_for_the_same_username()
    {
        await _service.InvalidateAsync("admin");
        await Task.Delay(10);
        var issuedAt = DateTimeOffset.UtcNow;
        await Task.Delay(10);
        await _service.InvalidateAsync("admin");

        Assert.False(await _service.IsValidAsync("admin", issuedAt));
        Assert.Single(await _db.SessionInvalidations.ToListAsync());
    }
}
