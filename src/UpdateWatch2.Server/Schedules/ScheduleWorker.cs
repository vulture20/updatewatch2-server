namespace UpdateWatch2.Server.Schedules;

/// <summary>
/// This project's eighth server-side <see cref="BackgroundService"/>, after
/// <see cref="AgentUpdates.AgentUpdateCheckWorker"/>/<see cref="Audit.AuditLogRetentionWorker"/>/
/// <see cref="Certificates.CertificateExpiryWorker"/>/<see cref="Db.DatabaseVacuumWorker"/>/
/// <see cref="Notifications.UpdateThresholdNotificationWorker"/>/<see cref="Notifications.AgentOfflineNotificationWorker"/>/
/// <see cref="Notifications.SmtpHealthCheckWorker"/>. Checks every
/// <see cref="_checkInterval"/> (1 minute in production — deliberately much
/// finer than every other worker's 6h/24h housekeeping cadence, since real
/// wall-clock times need to be hit reasonably precisely, the same
/// time-sensitivity reasoning the agent's own <c>HeartbeatWorker</c>/
/// <c>UpdateCheckWorker</c> already apply). Two independent passes per
/// tick: fire whatever's due, then expire whatever's overdue — order
/// doesn't matter between the two (a schedule that just fired this same
/// tick can't also be overdue from an earlier deadline).
/// </summary>
public class ScheduleWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduleWorker> logger,
    TimeSpan? checkInterval = null) : BackgroundService
{
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromMinutes(1);

    private readonly TimeSpan _checkInterval = checkInterval ?? DefaultCheckInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IScheduleService>();
                await service.FireDueSchedulesAsync(stoppingToken);
                await service.ExpireMissedAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Schedule check failed unexpectedly.");
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
