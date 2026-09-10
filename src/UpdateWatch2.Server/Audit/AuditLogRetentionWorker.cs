using UpdateWatch2.Server.Admin;

namespace UpdateWatch2.Server.Audit;

/// <summary>
/// Periodically discards audit log entries older than the admin-configured
/// <see cref="IAdminSettingsStore.AuditLogRetentionDays"/> (0 = unlimited,
/// never discard) — this project's second server-side <see cref="BackgroundService"/>,
/// after <see cref="AgentUpdates.AgentUpdateCheckWorker"/>. Runs a check
/// immediately on startup, then every <see cref="_checkInterval"/> (24
/// hours in production, per <see cref="DefaultCheckInterval"/>) — not
/// admin-configurable itself, unlike the retention threshold, since this
/// is pure housekeeping with no reason for an admin to tune its cadence
/// the way <c>AgentAutoUpdateCheckIntervalHours</c> is tuned for GitHub
/// API rate limits; the constructor parameter exists purely so a test can
/// inject a near-instant interval instead of actually waiting a day to
/// observe more than one loop iteration. Checking immediately at startup
/// (rather than only waiting out the first interval) matters for the same
/// reason <see cref="AgentUpdates.AgentUpdateCheckWorker"/> does: an admin
/// who just lowered the retention while the server was stopped shouldn't
/// have to wait up to a full day for it to actually take effect.
///
/// A new <see cref="IServiceScope"/> per tick, resolving
/// <see cref="IAuditLogService"/> from it, since that service depends on
/// the scoped <c>AppDbContext</c> — this <see cref="BackgroundService"/>
/// itself is a singleton for the process's whole lifetime, so it can't
/// hold a scoped dependency directly (same reasoning as
/// <see cref="AgentUpdates.AgentUpdateCheckWorker"/>).
/// </summary>
public class AuditLogRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IAdminSettingsStore settingsStore,
    ILogger<AuditLogRetentionWorker> logger,
    TimeSpan? checkInterval = null) : BackgroundService
{
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromHours(24);

    private readonly TimeSpan _checkInterval = checkInterval ?? DefaultCheckInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var retentionDays = settingsStore.AuditLogRetentionDays;
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
                var deleted = await service.PurgeOlderThanAsync(retentionDays, stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation(
                        "Audit log retention cleanup discarded {Count} entr{Suffix} older than {RetentionDays} day(s).",
                        deleted, deleted == 1 ? "y" : "ies", retentionDays);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Audit log retention cleanup failed unexpectedly.");
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
