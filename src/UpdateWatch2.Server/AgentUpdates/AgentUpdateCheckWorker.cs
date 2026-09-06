using UpdateWatch2.Server.Admin;

namespace UpdateWatch2.Server.AgentUpdates;

/// <summary>
/// Periodically checks GitHub for a newer agent release (updatewatch2-server#14)
/// — this project's first server-side <see cref="BackgroundService"/>;
/// every other piece of periodic background behavior in this codebase
/// lives on the agent side (<c>HeartbeatWorker</c>/<c>UpdateCheckWorker</c>).
/// Runs on its own interval, independent of any agent's heartbeat
/// cadence, since this checks GitHub, not any particular agent — an
/// agent only ever learns about the result via <see cref="IAgentUpdateService.GetOfferForAsync"/>
/// on its own next heartbeat.
///
/// The interval itself (<see cref="AgentAutoUpdateOptions.CheckIntervalHours"/>,
/// admin-configurable) is re-read from <see cref="IAdminSettingsStore"/> —
/// a singleton, safe to hold directly, unlike <see cref="IAgentUpdateService"/>
/// below — fresh on every loop iteration rather than captured once at
/// startup, so an admin shortening or lengthening it takes effect on the
/// very next wait, the same live-reload behavior every other admin
/// setting already gets.
///
/// A new <see cref="IServiceScope"/> per tick, resolving
/// <see cref="IAgentUpdateService"/> from it, since that service depends
/// on the scoped <c>AppDbContext</c> — this <see cref="BackgroundService"/>
/// itself is a singleton for the process's whole lifetime, so it can't
/// hold a scoped dependency directly.
/// </summary>
public class AgentUpdateCheckWorker(
    IServiceScopeFactory scopeFactory,
    IAdminSettingsStore settingsStore,
    ILogger<AgentUpdateCheckWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAgentUpdateService>();
                var outcome = await service.CheckForUpdatesAsync(stoppingToken);
                if (outcome == AgentUpdateCheckOutcome.Failed)
                {
                    logger.LogWarning("Agent update check failed — see the persisted status for the reason.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Agent update check failed unexpectedly.");
            }

            try
            {
                // Math.Max, not the raw configured value: PUT /api/admin/settings
                // already rejects anything below 1, but a negative TimeSpan
                // makes Task.Delay throw ArgumentOutOfRangeException outside
                // this method's own try/catch (which only ever catches
                // OperationCanceledException) — defense in depth against a
                // stored value the API-level validation didn't produce (a
                // hand-edited row, a future migration with a bad default),
                // which would otherwise silently kill this loop for the rest
                // of the process's lifetime.
                var intervalHours = Math.Max(0, settingsStore.AgentAutoUpdate.CheckIntervalHours);
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
