using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Notifications;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Certificates;

public class CertificateExpiryWorkerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"uw2-cert-expiry-worker-test-{Guid.NewGuid()}.sqlite");
    private readonly string _certSourceDirectory = Path.Combine(Path.GetTempPath(), $"uw2-cert-expiry-worker-source-{Guid.NewGuid()}");
    // A throwaway real CA purely to harvest real, distinct X509Certificate2
    // instances (a ~10-year root, a ~2-year server leaf) for the fake CA
    // below to hand back — simpler and more realistic than hand-rolling
    // certificate generation in this test file too.
    private readonly InternalCertificateAuthority _certSource;
    private readonly FakeCertificateAuthority _certificateAuthority = new();
    private readonly FakeEmailNotificationService _email = new();
    private readonly FakeAdminSettingsStore _settingsStore = new();
    private readonly ServiceProvider _services;

    public CertificateExpiryWorkerTests()
    {
        _certSource = new InternalCertificateAuthority(_certSourceDirectory);
        _certificateAuthority.RootCertificate = _certSource.RootCertificate;

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddSingleton<IEmailNotificationService>(_email);
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    public void Dispose()
    {
        _services.Dispose();
        File.Delete(_dbPath);
        if (Directory.Exists(_certSourceDirectory))
        {
            Directory.Delete(_certSourceDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Renews_the_server_leaf_and_audit_logs_it_even_with_no_notification_recipient_configured()
    {
        var renewed = _certSource.EnsureServerLeaf("no-recipient-host");
        _certificateAuthority.RenewalResult = renewed;
        // Smtp deliberately left at its default (unconfigured) — the
        // renewal itself must still happen and still be audit-logged.
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _certificateAuthority.RenewCallCount >= 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_email.SentNotifications);
        var page = await GetAuditPageAsync();
        Assert.Contains(page.Entries, e => e.Action == "certificate.server-leaf.renewed");
    }

    [Fact]
    public async Task Sends_a_renewal_notice_email_when_a_recipient_is_configured()
    {
        var renewed = _certSource.EnsureServerLeaf("recipient-host");
        _certificateAuthority.RenewalResult = renewed;
        _settingsStore.Smtp = new SmtpOptions { Host = "smtp.example.com", FromAddress = "uw2@example.com", NotificationRecipientAddress = "alerts@example.com" };
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _email.SentNotifications.Count >= 1);
        await worker.StopAsync(CancellationToken.None);

        var sent = Assert.Single(_email.SentNotifications);
        Assert.Equal("alerts@example.com", sent.To);
        Assert.Contains("renewed", sent.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sends_no_email_when_CertificateExpiryNotificationsEnabled_is_off_even_with_a_recipient_configured()
    {
        var renewed = _certSource.EnsureServerLeaf("disabled-toggle-host");
        _certificateAuthority.RenewalResult = renewed;
        _settingsStore.Smtp = new SmtpOptions { Host = "smtp.example.com", FromAddress = "uw2@example.com", NotificationRecipientAddress = "alerts@example.com" };
        _settingsStore.Certificate = new CertificateOptions { CertificateExpiryNotificationsEnabled = false, CertificateExpiryWarningLeadDays = 5000 };
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        // Wait for the actual audit entries, not just RenewCallCount — that
        // counter increments synchronously inside RenewServerLeafIfNearExpiry,
        // well before the async audit-log writes for either condition have
        // necessarily completed, so polling on it alone and then
        // immediately calling StopAsync could race the still-in-flight
        // writes against StopAsync's own cancellation of the shared token
        // their SaveChangesAsync calls observe (found while touching a
        // different worker's tests this same session — see
        // UpdateThresholdNotificationWorkerTests for the identical fix).
        await WaitUntilAsync(async () =>
        {
            var entries = (await GetAuditPageAsync()).Entries;
            return entries.Any(e => e.Action == "certificate.server-leaf.renewed")
                && entries.Any(e => e.Action == "certificate.ca-root.expiry-warning");
        });
        await worker.StopAsync(CancellationToken.None);

        // The renewal itself and the CA-root-within-window condition both
        // still apply (leadDays: 5000 makes the root "near expiry" too) —
        // only the toggle being off must be what suppresses the email.
        Assert.Empty(_email.SentNotifications);
        var page = await GetAuditPageAsync();
        Assert.Contains(page.Entries, e => e.Action == "certificate.server-leaf.renewed");
        Assert.Contains(page.Entries, e => e.Action == "certificate.ca-root.expiry-warning");
    }

    [Fact]
    public async Task Retries_the_renewal_notice_email_on_a_later_tick_after_the_first_send_attempt_fails()
    {
        var renewed = _certSource.EnsureServerLeaf("retry-host");
        _certificateAuthority.RenewalResult = renewed;
        _email.ThrowOnSend = true;
        _settingsStore.Smtp = new SmtpOptions { Host = "smtp.example.com", FromAddress = "uw2@example.com", NotificationRecipientAddress = "alerts@example.com" };
        var worker = CreateWorker(TimeSpan.Zero);

        await worker.StartAsync(CancellationToken.None);
        // The renewal itself (a one-shot event) has already happened and
        // the first, failing send attempt has already been made — proven
        // by RenewCallCount advancing well past the renewal tick, not by
        // the email ever having actually sent.
        await WaitUntilAsync(() => _email.SendAttemptCount >= 1 && _certificateAuthority.RenewCallCount > 2);
        Assert.Empty(_email.SentNotifications);

        _email.ThrowOnSend = false;
        await WaitUntilAsync(() => _email.SentNotifications.Count >= 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(_email.SentNotifications);
    }

    [Fact]
    public async Task Warns_about_the_CA_root_when_it_is_within_the_configured_lead_time()
    {
        // The real root is ~10 years out — a lead time longer than that
        // makes "within the lead time of NotAfter" trivially true, the
        // same trick InternalCertificateAuthorityTests uses, without
        // fabricating an actually-near-expiry root. Not reachable through
        // the validated admin API (which caps this at 365), but this test
        // talks to the worker/FakeAdminSettingsStore directly.
        _settingsStore.Certificate = new CertificateOptions { CertificateExpiryWarningLeadDays = 5000 };
        _settingsStore.Smtp = new SmtpOptions { Host = "smtp.example.com", FromAddress = "uw2@example.com", NotificationRecipientAddress = "alerts@example.com" };
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _email.SentNotifications.Count >= 1);
        await worker.StopAsync(CancellationToken.None);

        var sent = Assert.Single(_email.SentNotifications);
        Assert.Contains("CA root", sent.Subject);
        var page = await GetAuditPageAsync();
        Assert.Contains(page.Entries, e => e.Action == "certificate.ca-root.expiry-warning");
    }

    [Fact]
    public async Task Does_not_warn_about_the_CA_root_when_it_is_not_near_expiry()
    {
        _settingsStore.Certificate = new CertificateOptions { CertificateExpiryWarningLeadDays = 1 };
        _settingsStore.Smtp = new SmtpOptions { Host = "smtp.example.com", FromAddress = "uw2@example.com", NotificationRecipientAddress = "alerts@example.com" };
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _certificateAuthority.RenewCallCount >= 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_email.SentNotifications);
        var page = await GetAuditPageAsync();
        Assert.DoesNotContain(page.Entries, e => e.Action == "certificate.ca-root.expiry-warning");
    }

    [Fact]
    public async Task Warns_about_the_CA_root_only_once_per_root_generation_not_on_every_tick()
    {
        _settingsStore.Certificate = new CertificateOptions { CertificateExpiryWarningLeadDays = 5000 };
        _settingsStore.Smtp = new SmtpOptions { Host = "smtp.example.com", FromAddress = "uw2@example.com", NotificationRecipientAddress = "alerts@example.com" };
        var worker = CreateWorker(TimeSpan.Zero);

        await worker.StartAsync(CancellationToken.None);
        // Several ticks have run (RenewCallCount advances on every tick
        // regardless of outcome) — the CA warning must not have repeated.
        await WaitUntilAsync(() => _certificateAuthority.RenewCallCount > 5);
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(_email.SentNotifications);
    }

    private CertificateExpiryWorker CreateWorker(TimeSpan checkInterval) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), _settingsStore, _certificateAuthority, NullLogger<CertificateExpiryWorker>.Instance, checkInterval);

    private async Task<AuditLogPageDto> GetAuditPageAsync()
    {
        using var scope = _services.CreateScope();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        return await auditLog.GetPageAsync(1, 50);
    }

    /// <summary>Same polling helper AuditLogRetentionWorkerTests/AgentUpdateCheckWorkerTests use — StartAsync only schedules ExecuteAsync, it doesn't wait for it to run.</summary>
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

    private class FakeCertificateAuthority : ICertificateAuthority
    {
        public X509Certificate2 RootCertificate { get; set; } = null!;

        /// <summary>What the next <see cref="RenewServerLeafIfNearExpiry"/> call returns — cleared after one call, mimicking the real implementation's one-shot-per-renewal behavior.</summary>
        public X509Certificate2? RenewalResult { get; set; }

        public int RenewCallCount { get; private set; }

        public X509Certificate2? PreviousRootCertificate => throw new NotSupportedException();

        public X509Certificate2? PendingRootCertificate => throw new NotSupportedException();

        public X509Certificate2Collection TrustedRootCertificates => throw new NotSupportedException();

        public X509Certificate2Collection AllKnownRootCertificates => throw new NotSupportedException();

        public X509Certificate2 CurrentServerLeaf => throw new NotSupportedException();

        public X509Certificate2 EnsureServerLeaf(string sanHostname) => throw new NotSupportedException();

        public X509Certificate2? RenewServerLeafIfNearExpiry(TimeSpan leadTime)
        {
            RenewCallCount++;
            var result = RenewalResult;
            RenewalResult = null;
            return result;
        }

        public IssuedCertificate IssueAgentLeaf(string hostname, TimeSpan validity) => throw new NotSupportedException();

        public CaRotationStatus GetRotationStatus() => throw new NotSupportedException();

        public X509Certificate2 PrepareRotation() => throw new NotSupportedException();

        public void ActivateRotation() => throw new NotSupportedException();

        public void RetirePreviousRoot() => throw new NotSupportedException();
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
