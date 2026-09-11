using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Db;

namespace UpdateWatch2.Server.Tests.Auth;

public class AdminAccountServiceTests : IDisposable
{
    private const string ResetEnvVar = "UPDATEWATCH2_RESET_ADMIN_PASSWORD";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-admin-account-test-{Guid.NewGuid()}.sqlite");
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLog;

    public AdminAccountServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();
        _auditLog = new AuditLogService(_db);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ResetEnvVar, null);
        _db.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task EnsureSeededAsync_creates_one_account_with_a_policy_valid_password_and_logs_it_once()
    {
        var capturingLogger = new CapturingLogger();
        var service = new AdminAccountService(_db, _auditLog, capturingLogger);

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
        var service = new AdminAccountService(_db, _auditLog, new CapturingLogger());

        await service.EnsureSeededAsync();
        await service.EnsureSeededAsync();

        Assert.Equal(1, await _db.AdminAccounts.CountAsync());
    }

    [Fact]
    public async Task VerifyPasswordAsync_rejects_the_wrong_password()
    {
        var service = new AdminAccountService(_db, _auditLog, new CapturingLogger());
        await service.EnsureSeededAsync();

        Assert.False(await service.VerifyPasswordAsync(AdminAccountService.DefaultUsername, "definitely-wrong"));
    }

    [Fact]
    public async Task VerifyPasswordAsync_rejects_an_unknown_username()
    {
        var service = new AdminAccountService(_db, _auditLog, new CapturingLogger());
        await service.EnsureSeededAsync();

        Assert.False(await service.VerifyPasswordAsync("nobody", "irrelevant"));
    }

    [Fact]
    public async Task ResetPasswordFromEnvironmentIfConfiguredAsync_is_a_no_op_when_the_variable_is_unset()
    {
        var service = new AdminAccountService(_db, _auditLog, new CapturingLogger());
        await service.EnsureSeededAsync();

        await service.ResetPasswordFromEnvironmentIfConfiguredAsync();

        var account = Assert.Single(await _db.AdminAccounts.ToListAsync());
        Assert.Null(account.PasswordResetEnvValueHash);
        Assert.Empty(await _db.AuditLogEntries.ToListAsync());
    }

    [Fact]
    public async Task ResetPasswordFromEnvironmentIfConfiguredAsync_resets_the_password_logs_a_warning_and_audits_it()
    {
        var capturingLogger = new CapturingLogger();
        var service = new AdminAccountService(_db, _auditLog, capturingLogger);
        await service.EnsureSeededAsync();

        var newPassword = PasswordPolicy.Generate();
        Environment.SetEnvironmentVariable(ResetEnvVar, newPassword);

        await service.ResetPasswordFromEnvironmentIfConfiguredAsync();

        Assert.True(await service.VerifyPasswordAsync(AdminAccountService.DefaultUsername, newPassword));
        var account = Assert.Single(await _db.AdminAccounts.ToListAsync());
        Assert.NotNull(account.PasswordResetEnvValueHash);
        Assert.NotNull(account.PasswordChangedAt);
        Assert.Contains(capturingLogger.Messages, m => m.Contains(ResetEnvVar, StringComparison.Ordinal));
        var auditEntry = Assert.Single(await _db.AuditLogEntries.ToListAsync());
        Assert.Equal("admin.password.reset-via-environment", auditEntry.Action);
    }

    [Fact]
    public async Task ResetPasswordFromEnvironmentIfConfiguredAsync_applying_the_same_value_twice_is_a_no_op_the_second_time()
    {
        var service = new AdminAccountService(_db, _auditLog, new CapturingLogger());
        await service.EnsureSeededAsync();

        var newPassword = PasswordPolicy.Generate();
        Environment.SetEnvironmentVariable(ResetEnvVar, newPassword);

        await service.ResetPasswordFromEnvironmentIfConfiguredAsync();
        var hashAfterFirstCall = Assert.Single(await _db.AdminAccounts.ToListAsync()).PasswordResetEnvValueHash;

        await service.ResetPasswordFromEnvironmentIfConfiguredAsync();

        var account = Assert.Single(await _db.AdminAccounts.ToListAsync());
        Assert.Equal(hashAfterFirstCall, account.PasswordResetEnvValueHash);
        // Only the one reset should have been audited — a second call with
        // the identical value must not repeat it.
        Assert.Single(await _db.AuditLogEntries.ToListAsync());
        Assert.True(await service.VerifyPasswordAsync(AdminAccountService.DefaultUsername, newPassword));
    }

    [Fact]
    public async Task ResetPasswordFromEnvironmentIfConfiguredAsync_resets_again_when_the_value_changes()
    {
        var service = new AdminAccountService(_db, _auditLog, new CapturingLogger());
        await service.EnsureSeededAsync();

        var firstPassword = PasswordPolicy.Generate();
        Environment.SetEnvironmentVariable(ResetEnvVar, firstPassword);
        await service.ResetPasswordFromEnvironmentIfConfiguredAsync();

        var secondPassword = PasswordPolicy.Generate();
        Environment.SetEnvironmentVariable(ResetEnvVar, secondPassword);
        await service.ResetPasswordFromEnvironmentIfConfiguredAsync();

        Assert.False(await service.VerifyPasswordAsync(AdminAccountService.DefaultUsername, firstPassword));
        Assert.True(await service.VerifyPasswordAsync(AdminAccountService.DefaultUsername, secondPassword));
        Assert.Equal(2, await _db.AuditLogEntries.CountAsync());
    }

    [Fact]
    public async Task ResetPasswordFromEnvironmentIfConfiguredAsync_rejects_a_value_that_fails_the_password_policy()
    {
        var capturingLogger = new CapturingLogger();
        var service = new AdminAccountService(_db, _auditLog, capturingLogger);
        await service.EnsureSeededAsync();

        Environment.SetEnvironmentVariable(ResetEnvVar, "too-short1!");

        await service.ResetPasswordFromEnvironmentIfConfiguredAsync();

        var account = Assert.Single(await _db.AdminAccounts.ToListAsync());
        Assert.Null(account.PasswordResetEnvValueHash);
        Assert.False(await service.VerifyPasswordAsync(AdminAccountService.DefaultUsername, "too-short1!"));
        Assert.Empty(await _db.AuditLogEntries.ToListAsync());
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
