namespace UpdateWatch2.Server.AgentUpdates;

/// <summary>
/// Periodically checks GitHub for a newer agent release (updatewatch2-server#14)
/// — this project's first server-side <see cref="BackgroundService"/>;
/// every other piece of periodic background behavior in this codebase
/// lives on the agent side (<c>HeartbeatWorker</c>/<c>UpdateCheckWorker</c>).
/// Runs on its own fixed interval, independent of any agent's heartbeat
/// cadence, since this checks GitHub, not any particular agent — an
/// agent only ever learns about the result via <see cref="IAgentUpdateService.GetOfferForAsync"/>
/// on its own next heartbeat.
///
/// A new <see cref="IServiceScope"/> per tick, resolving
/// <see cref="IAgentUpdateService"/> from it, since that service depends
/// on the scoped <c>AppDbContext</c> — this <see cref="BackgroundService"/>
/// itself is a singleton for the process's whole lifetime, so it can't
/// hold a scoped dependency directly.
/// </summary>
public class AgentUpdateCheckWorker(IServiceScopeFactory scopeFactory, ILogger<AgentUpdateCheckWorker> logger) : BackgroundService
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

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
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
