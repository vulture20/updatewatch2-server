using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Db;

namespace UpdateWatch2.Server.Tests.Auth;

public class AdminAccountServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-admin-account-test-{Guid.NewGuid()}.sqlite");
    private readonly AppDbContext _db;

    public AdminAccountServiceTests()
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
    public async Task EnsureSeededAsync_creates_one_account_with_a_policy_valid_password_and_logs_it_once()
    {
        var capturingLogger = new CapturingLogger();
        var service = new AdminAccountService(_db, capturingLogger);

        await service.EnsureSeededAsync();

        var accounts = await _db.AdminAccounts.ToListAsync();
        var account = Assert.Single(accounts);
        Assert.Equal(AdminAccountService.DefaultUsername, account.Username);

        Assert.Single(capturingLogger.Messages);
        var loggedPassword = ExtractLoggedPassword(capturingLogger.Messages[0]);
        Assert.True(PasswordPolicy.IsValid(loggedPassword));
        Assert.True(await service.VerifyPasswordAsync(AdminAccountService.DefaultUsername, loggedPassword));
    }

    [Fact]
    public async Task EnsureSeededAsync_is_idempotent()
    {
        var service = new AdminAccountService(_db, new CapturingLogger());

        await service.EnsureSeededAsync();
        await service.EnsureSeededAsync();

        Assert.Equal(1, await _db.AdminAccounts.CountAsync());
    }

    [Fact]
    public async Task VerifyPasswordAsync_rejects_the_wrong_password()
    {
        var service = new AdminAccountService(_db, new CapturingLogger());
        await service.EnsureSeededAsync();

        Assert.False(await service.VerifyPasswordAsync(AdminAccountService.DefaultUsername, "definitely-wrong"));
    }

    [Fact]
    public async Task VerifyPasswordAsync_rejects_an_unknown_username()
    {
        var service = new AdminAccountService(_db, new CapturingLogger());
        await service.EnsureSeededAsync();

        Assert.False(await service.VerifyPasswordAsync("nobody", "irrelevant"));
    }

    private static string ExtractLoggedPassword(string message)
    {
        // "...password): {password} — change this..." — pull out the token between "): " and " —".
        var start = message.IndexOf("): ", StringComparison.Ordinal) + 3;
        var end = message.IndexOf(" —", start, StringComparison.Ordinal);
        return message[start..end];
    }

    private class CapturingLogger : ILogger<AdminAccountService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
