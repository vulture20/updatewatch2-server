using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Notifications;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Notifications;

public class UpdateThresholdNotificationWorkerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"uw2-update-threshold-worker-test-{Guid.NewGuid()}.sqlite");
    private readonly FakeEmailNotificationService _email = new();
    private readonly FakeAdminSettingsStore _settingsStore = new();
    private readonly ServiceProvider _services;

    public UpdateThresholdNotificationWorkerTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddSingleton<IEmailNotificationService>(_email);
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();

        _settingsStore.Smtp = new SmtpOptions { Host = "smtp.example.com", FromAddress = "uw2@example.com", NotificationRecipientAddress = "alerts@example.com" };
    }

    public void Dispose()
    {
        _services.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task Sends_a_notification_when_a_single_machine_crosses_the_updates_per_machine_threshold()
    {
        await SeedAgentWithUpdatesAsync("host-a", updateCount: 5);
        _settingsStore.NotificationThresholds = new NotificationThresholdOptions { UpdatesPerMachine = 5, AffectedMachines = 100 };
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        // Wait for the audit entry, not just the email — it's written
        // AFTER the email send completes within the same tick, so waiting
        // only on the email and then immediately calling StopAsync can
        // race the still-in-flight audit-log write against StopAsync's own
        // cancellation of the shared token that write's SaveChangesAsync
        // call observes.
        await WaitUntilAsync(async () => (await GetAuditPageAsync()).Entries.Any(e => e.Action == "notifications.updates-per-machine-threshold.crossed"));
        await worker.StopAsync(CancellationToken.None);

        var sent = Assert.Single(_email.SentNotifications);
        Assert.Equal("alerts@example.com", sent.To);
        Assert.Contains("updates-per-machine", sent.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sends_a_notification_when_the_affected_machine_count_crosses_its_threshold()
    {
        await SeedAgentWithUpdatesAsync("host-a", updateCount: 1);
        await SeedAgentWithUpdatesAsync("host-b", updateCount: 1);
        _settingsStore.NotificationThresholds = new NotificationThresholdOptions { UpdatesPerMachine = 100, AffectedMachines = 2 };
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => (await GetAuditPageAsync()).Entries.Any(e => e.Action == "notifications.affected-machines-threshold.crossed"));
        await worker.StopAsync(CancellationToken.None);

        var sent = Assert.Single(_email.SentNotifications);
        Assert.Contains("affected-machines", sent.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Does_not_notify_when_neither_threshold_is_crossed()
    {
        await SeedAgentWithUpdatesAsync("host-a", updateCount: 2);
        _settingsStore.NotificationThresholds = new NotificationThresholdOptions { UpdatesPerMachine = 5, AffectedMachines = 10 };
        var worker = CreateWorker(TimeSpan.Zero);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_email.SentNotifications);
    }

    [Fact]
    public async Task Excludes_updates_matching_an_active_filter_from_both_thresholds()
    {
        var agent = await SeedAgentWithUpdatesAsync("host-a", updateCount: 0);
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UpdateItems.Add(new UpdateItem { AgentId = agent.Id, Title = "Security Intelligence-Update für Microsoft Defender Antivirus" });
            db.UpdateFilters.Add(new UpdateFilter { Name = "Defender", Pattern = "Defender Antivirus" });
            await db.SaveChangesAsync();
        }
        _settingsStore.NotificationThresholds = new NotificationThresholdOptions { UpdatesPerMachine = 1, AffectedMachines = 1 };
        var worker = CreateWorker(TimeSpan.Zero);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_email.SentNotifications);
    }

    [Fact]
    public async Task Sends_no_email_when_the_updates_per_machine_checkbox_is_disabled_even_when_crossed()
    {
        await SeedAgentWithUpdatesAsync("host-a", updateCount: 10);
        _settingsStore.NotificationThresholds = new NotificationThresholdOptions { UpdatesPerMachine = 5, UpdatesPerMachineEnabled = false, AffectedMachines = 100 };
        var worker = CreateWorker(TimeSpan.Zero);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_email.SentNotifications);
    }

    [Fact]
    public async Task Notifies_only_once_while_continuously_crossed_then_again_after_clearing_and_re_crossing()
    {
        var agent = await SeedAgentWithUpdatesAsync("host-a", updateCount: 5);
        _settingsStore.NotificationThresholds = new NotificationThresholdOptions { UpdatesPerMachine = 5, AffectedMachines = 100 };
        var worker = CreateWorker(TimeSpan.FromMilliseconds(30));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _email.SentNotifications.Count >= 1);
        // Several more ticks pass while still crossed — must not re-notify.
        await Task.Delay(150);
        Assert.Single(_email.SentNotifications);

        // Drop back below the threshold.
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var items = await db.UpdateItems.Where(u => u.AgentId == agent.Id).ToListAsync();
            db.UpdateItems.RemoveRange(items);
            await db.SaveChangesAsync();
        }
        await Task.Delay(150);
        Assert.Single(_email.SentNotifications);

        // Cross it again — a fresh notification must fire.
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 5; i++)
            {
                db.UpdateItems.Add(new UpdateItem { AgentId = agent.Id, Title = $"Update {i}" });
            }
            await db.SaveChangesAsync();
        }
        await WaitUntilAsync(() => _email.SentNotifications.Count >= 2);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, _email.SentNotifications.Count);
    }

    [Fact]
    public async Task Retries_the_notification_on_a_later_tick_after_the_first_send_attempt_fails()
    {
        await SeedAgentWithUpdatesAsync("host-a", updateCount: 5);
        _settingsStore.NotificationThresholds = new NotificationThresholdOptions { UpdatesPerMachine = 5, AffectedMachines = 100 };
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
        await SeedAgentWithUpdatesAsync("host-a", updateCount: 5);
        _settingsStore.Smtp = new SmtpOptions { Host = "smtp.example.com", FromAddress = "uw2@example.com" };
        _settingsStore.NotificationThresholds = new NotificationThresholdOptions { UpdatesPerMachine = 5, AffectedMachines = 100 };
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => (await GetAuditPageAsync()).Entries.Any(e => e.Action == "notifications.updates-per-machine-threshold.crossed"));
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_email.SentNotifications);
    }

    private UpdateThresholdNotificationWorker CreateWorker(TimeSpan checkInterval) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), _settingsStore, NullLogger<UpdateThresholdNotificationWorker>.Instance, checkInterval);

    private async Task<Agent> SeedAgentWithUpdatesAsync(string hostname, int updateCount)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = new Agent { Hostname = hostname, Approved = true };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        for (var i = 0; i < updateCount; i++)
        {
            db.UpdateItems.Add(new UpdateItem { AgentId = agent.Id, Title = $"Update {i}" });
        }
        await db.SaveChangesAsync();

        return agent;
    }

    private async Task<AuditLogPageDto> GetAuditPageAsync()
    {
        using var scope = _services.CreateScope();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        return await auditLog.GetPageAsync(1, 50);
    }

    /// <summary>Same polling helper CertificateExpiryWorkerTests/AuditLogRetentionWorkerTests use — StartAsync only schedules ExecuteAsync, it doesn't wait for it to run.</summary>
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
