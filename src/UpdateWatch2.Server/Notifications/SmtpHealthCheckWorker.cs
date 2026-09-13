namespace UpdateWatch2.Server.Notifications;

/// <summary>
/// Periodically refreshes <see cref="ISmtpHealthCache"/> from
/// <see cref="IEmailNotificationService.IsHealthyAsync"/> — this project's
/// seventh server-side <see cref="BackgroundService"/>, after
/// <see cref="AgentUpdates.AgentUpdateCheckWorker"/>,
/// <see cref="Audit.AuditLogRetentionWorker"/>,
/// <see cref="Certificates.CertificateExpiryWorker"/>,
/// <see cref="Db.DatabaseVacuumWorker"/>,
/// <see cref="UpdateThresholdNotificationWorker"/>, and
/// <see cref="AgentOfflineNotificationWorker"/> — same shape as all six: a
/// check immediately on startup, then every <see cref="_checkInterval"/>.
/// Exists specifically so <c>GET /api/admin/notifications/smtp-health</c>
/// never itself performs the live TCP probe
/// <see cref="IEmailNotificationService.IsHealthyAsync"/> does — see
/// <see cref="ISmtpHealthCache"/>'s own doc comment for why that trade-off
/// was chosen over checking on every page load/poll
/// (updatewatch2-server#12).
///
/// 5 minutes, not admin-configurable — tighter than the 24-hour cadence
/// pure-housekeeping workers use (a mail server actually being down is
/// worth noticing reasonably promptly), but far coarser than
/// <see cref="AgentOfflineNotificationWorker"/>'s 5-minute-tick-against-a-
/// 15-minute-threshold reasoning, since there's no admin-configured
/// threshold here to stay ahead of — just "reasonably fresh".
/// </summary>
public class SmtpHealthCheckWorker(
    IServiceScopeFactory scopeFactory,
    ISmtpHealthCache cache,
    ILogger<SmtpHealthCheckWorker> logger,
    TimeSpan? checkInterval = null) : BackgroundService
{
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _checkInterval = checkInterval ?? DefaultCheckInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
                var isHealthy = await emailService.IsHealthyAsync(stoppingToken);
                cache.Update(isHealthy, DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // IsHealthyAsync itself already catches the network-level
                // exceptions a genuinely unreachable server produces and
                // returns false — reaching this branch means something
                // else went wrong (e.g. a DI resolution failure), not "SMTP
                // is down", so the cache is deliberately left holding
                // whatever it last had rather than being overwritten with
                // a possibly-misleading false.
                logger.LogWarning(ex, "SMTP health check failed unexpectedly — leaving the cached value unchanged.");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
