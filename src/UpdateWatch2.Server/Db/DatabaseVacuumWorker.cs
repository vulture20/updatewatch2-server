namespace UpdateWatch2.Server.Db;

/// <summary>
/// Periodically reclaims disk space SQLite's own page management
/// otherwise never returns to the OS on its own (see
/// <see cref="IDatabaseMaintenanceService"/>'s own doc comment) — this
/// project's fourth server-side <see cref="BackgroundService"/>, after
/// <see cref="AgentUpdates.AgentUpdateCheckWorker"/>,
/// <see cref="Audit.AuditLogRetentionWorker"/>, and
/// <see cref="Certificates.CertificateExpiryWorker"/>. Runs a check
/// immediately on startup, then every <see cref="_checkInterval"/> (24
/// hours in production, per <see cref="DefaultCheckInterval"/>) — not
/// admin-configurable, same reasoning as <c>AuditLogRetentionWorker</c>'s
/// own cadence: pure housekeeping, nothing for an admin to tune. The
/// constructor parameter exists purely so a test can inject a near-
/// instant interval instead of actually waiting a day to observe more
/// than one loop iteration.
///
/// A new <see cref="IServiceScope"/> per tick, resolving
/// <see cref="IDatabaseMaintenanceService"/> from it, since that service
/// depends on the scoped <c>AppDbContext</c> — this
/// <see cref="BackgroundService"/> itself is a singleton for the
/// process's whole lifetime, so it can't hold a scoped dependency
/// directly (same reasoning as every other server-side worker here).
/// </summary>
public class DatabaseVacuumWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseVacuumWorker> logger,
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
                using var scope = scopeFactory.CreateScope();
                var maintenance = scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();
                await maintenance.IncrementalVacuumAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Incremental database vacuum failed — will retry on the next scheduled pass.");
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
