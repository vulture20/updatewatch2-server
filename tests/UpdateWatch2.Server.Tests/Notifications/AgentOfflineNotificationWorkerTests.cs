using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Notifications;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Notifications;

public class AgentOfflineNotificationWorkerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"uw2-agent-offline-worker-test-{Guid.NewGuid()}.sqlite");
    private readonly FakeEmailNotificationService _email = new();
    private readonly FakeAdminSettingsStore _settingsStore = new();
    private readonly ServiceProvider _services;

    public AgentOfflineNotificationWorkerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddSingleton<IEmailNotificationService>(_email);
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();

        _settingsStore.Smtp = new SmtpOptions { Host = "smtp.example.com", FromAddress = "uw2@example.com", NotificationRecipientAddress = "alerts@example.com" };
        _settingsStore.AgentOffline = new AgentOfflineOptions { ThresholdMinutes = 15 };
        _ = options; // keep the options var referenced for clarity of Data Source reuse above
    }

    public void Dispose()
    {
        _services.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task Sends_a_went_offline_email_for_an_agent_whose_last_heartbeat_exceeds_the_threshold()
    {
        await SeedAgentAsync("stale-host", DateTimeOffset.UtcNow.AddMinutes(-20));
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => (await GetAuditPageAsync()).Entries.Any(e => e.Action == "agent.offline.detected"));
        await worker.StopAsync(CancellationToken.None);

        var sent = Assert.Single(_email.SentNotifications);
        Assert.Equal("alerts@example.com", sent.To);
        Assert.Contains("stale-host", sent.Subject);
        Assert.Contains("went offline", sent.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Never_notifies_about_an_agent_that_has_no_heartbeat_at_all_yet()
    {
        await SeedAgentAsync("never-alive-host", lastAliveAt: null);
        var worker = CreateWorker(TimeSpan.Zero);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_email.SentNotifications);
    }

    [Fact]
    public async Task Does_not_notify_while_the_agent_is_within_the_threshold()
    {
        await SeedAgentAsync("healthy-host", DateTimeOffset.UtcNow.AddMinutes(-5));
        var worker = CreateWorker(TimeSpan.Zero);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_email.SentNotifications);
    }

    [Fact]
    public async Task Sends_a_back_online_email_once_a_previously_flagged_agent_heartbeats_again()
    {
        var agentId = await SeedAgentAsync("recovering-host", DateTimeOffset.UtcNow.AddMinutes(-20));
        var worker = CreateWorker(TimeSpan.FromMilliseconds(30));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _email.SentNotifications.Count >= 1);

        // Heartbeat lands — back within the threshold now.
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
            agent.LastAliveAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await WaitUntilAsync(async () => (await GetAuditPageAsync()).Entries.Any(e => e.Action == "agent.offline.recovered"));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, _email.SentNotifications.Count);
        Assert.Contains("back online", _email.SentNotifications[1].Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Notifies_only_once_while_continuously_offline()
    {
        var agentId = await SeedAgentAsync("continuously-offline-host", DateTimeOffset.UtcNow.AddMinutes(-20));
        var worker = CreateWorker(TimeSpan.FromMilliseconds(20));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _email.SentNotifications.Count >= 1);
        await Task.Delay(150); // several more ticks pass while still offline
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(_email.SentNotifications);
        _ = agentId;
    }

    [Fact]
    public async Task Sends_no_email_when_the_offline_notification_checkbox_is_disabled_but_still_flags_it_handled()
    {
        _settingsStore.AgentOffline = new AgentOfflineOptions { ThresholdMinutes = 15, OfflineNotificationEnabled = false };
        await SeedAgentAsync("toggle-off-host", DateTimeOffset.UtcNow.AddMinutes(-20));
        var worker = CreateWorker(TimeSpan.Zero);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_email.SentNotifications);
    }

    [Fact]
    public async Task Sends_no_recovery_email_when_that_checkbox_is_disabled_even_though_the_offline_checkbox_is_on()
    {
        _settingsStore.AgentOffline = new AgentOfflineOptions { ThresholdMinutes = 15, OnlineRecoveryNotificationEnabled = false };
        var agentId = await SeedAgentAsync("no-recovery-mail-host", DateTimeOffset.UtcNow.AddMinutes(-20));
        var worker = CreateWorker(TimeSpan.FromMilliseconds(30));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _email.SentNotifications.Count >= 1); // the offline email still sends

        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
            agent.LastAliveAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await WaitUntilAsync(async () => (await GetAuditPageAsync()).Entries.Any(e => e.Action == "agent.offline.recovered"));
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(_email.SentNotifications); // only the original "went offline" email
    }

    [Fact]
    public async Task Retries_the_notification_on_a_later_tick_after_the_first_send_attempt_fails()
    {
        await SeedAgentAsync("retry-host", DateTimeOffset.UtcNow.AddMinutes(-20));
        _email.ThrowOnSend = true;
        var worker = CreateWorker(TimeSpan.FromMilliseconds(20));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _email.SendAttemptCount >= 1);
        Assert.Empty(_email.SentNotifications);

        _email.ThrowOnSend = false;
        await WaitUntilAsync(() => _email.SentNotifications.Count >= 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(_email.SentNotifications);
    }

    [Fact]
    public async Task Audit_logs_immediately_with_no_email_attempt_when_no_recipient_is_configured()
    {
        _settingsStore.Smtp = new SmtpOptions { Host = "smtp.example.com", FromAddress = "uw2@example.com" };
        await SeedAgentAsync("no-recipient-host", DateTimeOffset.UtcNow.AddMinutes(-20));
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => (await GetAuditPageAsync()).Entries.Any(e => e.Action == "agent.offline.detected"));
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_email.SentNotifications);
    }

    private AgentOfflineNotificationWorker CreateWorker(TimeSpan checkInterval) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), _settingsStore, NullLogger<AgentOfflineNotificationWorker>.Instance, checkInterval);

    private async Task<int> SeedAgentAsync(string hostname, DateTimeOffset? lastAliveAt)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = new Agent { Hostname = hostname, Approved = true, LastAliveAt = lastAliveAt };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private async Task<AuditLogPageDto> GetAuditPageAsync()
    {
        using var scope = _services.CreateScope();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        return await auditLog.GetPageAsync(1, 50);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!await condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
    }

    private class FakeEmailNotificationService : IEmailNotificationService
    {
        public List<(string To, string Subject, string Body)> SentNotifications { get; } = [];

        public int SendAttemptCount { get; private set; }

        public bool ThrowOnSend { get; set; }

        public Task SendTestEmailAsync(string toAddress, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> IsHealthyAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task SendNotificationAsync(string toAddress, string subjectEn, string bodyEn, string subjectDe, string bodyDe, CancellationToken ct = default)
        {
            SendAttemptCount++;
            if (ThrowOnSend)
            {
                throw new InvalidOperationException("simulated SMTP failure");
            }

            SentNotifications.Add((toAddress, subjectEn, bodyEn));
            return Task.CompletedTask;
        }
    }
}
