using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Notifications;

/// <summary>
/// Periodically evaluates <see cref="Agents.AgentOfflineOptions.ThresholdMinutes"/>
/// per agent — at the user's explicit request ("Warnschwelle festlegen, ab
/// wann ein Client als offline gilt und diesen dann markieren und eventuell
/// per Mail informieren."). This project's sixth server-side
/// <see cref="BackgroundService"/>, after <see cref="AgentUpdates.AgentUpdateCheckWorker"/>/
/// <see cref="Audit.AuditLogRetentionWorker"/>/<see cref="Certificates.CertificateExpiryWorker"/>/
/// <see cref="Db.DatabaseVacuumWorker"/>/<see cref="UpdateThresholdNotificationWorker"/> —
/// same shape as all five: a check immediately on startup, then every
/// <see cref="_checkInterval"/>, re-reading the live admin-configured
/// threshold/toggles on every tick. Deliberately a tighter default cadence
/// (5 minutes) than those siblings, since the default threshold itself is
/// only 15 minutes — a coarser check interval would make the "went
/// offline" notification arrive uselessly late relative to the threshold
/// it's supposed to honor.
///
/// The icon/filter the admin UI shows is NEVER driven by this worker —
/// <see cref="Agents.AgentService"/> computes that live, on every request,
/// against the CURRENT threshold (see <see cref="Agents.AgentOfflineOptions"/>'s
/// own doc comment for why). This worker exists purely to decide when to
/// send an email, edge-triggered per agent via <see cref="Agent.OfflineCrossed"/>/
/// <see cref="Agent.OfflineNotifiedAt"/> — the identical fire-once-per-episode
/// pattern <see cref="UpdateThresholdNotificationWorker"/> already
/// established, just scoped per agent instead of one shared state row, and
/// covering two independently-toggleable directions (going offline,
/// coming back online — <see cref="Agents.AgentOfflineOptions.OfflineNotificationEnabled"/>/
/// <see cref="Agents.AgentOfflineOptions.OnlineRecoveryNotificationEnabled"/>,
/// both default true, at the user's explicit request for the recovery
/// notification to be independently switchable) rather than
/// <see cref="UpdateThresholdNotificationWorker"/>'s single direction.
///
/// An agent that has never heartbeated at all (<see cref="Agent.LastAliveAt"/>
/// null) is deliberately excluded from consideration here — it has no
/// "was online" state to transition away from, so notifying about it would
/// just be noise the moment a brand-new agent is approved, not a real
/// offline event. It still shows the offline icon in the UI, though — that
/// live computation has no equivalent exclusion, since "never seen online"
/// is itself worth flagging visually even when it's not worth emailing
/// about.
/// </summary>
public class AgentOfflineNotificationWorker(
    IServiceScopeFactory scopeFactory,
    IAdminSettingsStore settingsStore,
    ILogger<AgentOfflineNotificationWorker> logger,
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
                await CheckOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Agent offline notification check failed unexpectedly.");
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

    private async Task CheckOnceAsync(CancellationToken ct)
    {
        var offlineOptions = settingsStore.AgentOffline;
        var smtp = settingsStore.Smtp;
        var recipient = smtp.NotificationRecipientAddress;
        var canEmail = smtp.IsConfigured && !string.IsNullOrWhiteSpace(recipient);
        var threshold = TimeSpan.FromMinutes(offlineOptions.ThresholdMinutes);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        // Agents with no heartbeat at all yet are excluded here — see this
        // class's own doc comment for why.
        var agents = await db.Agents.Where(a => a.LastAliveAt != null).ToListAsync(ct);

        var changed = false;
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var agent in agents)
        {
            changed |= await CheckAgentAsync(scope, auditLog, agent, utcNow, threshold, offlineOptions, canEmail, recipient, ct);
        }

        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// The shared edge-triggered check/notify logic, mirroring
    /// <see cref="UpdateThresholdNotificationWorker.CheckConditionAsync"/>'s
    /// own shape — see this class's doc comment for how it differs (two
    /// independently-toggleable directions instead of one). Returns true if
    /// <paramref name="agent"/> was changed and needs saving.
    ///
    /// <see cref="Agent.OfflineCrossed"/> holds the last state this worker
    /// actually finished handling (notified, or decided not to email but
    /// still recorded) — NOT merely "is offline right now". A mismatch
    /// between that and the freshly computed <c>isOfflineNow</c> is exactly
    /// what "pending, not yet handled" means, and persists across ticks
    /// (untouched) until a send attempt actually succeeds or is skipped,
    /// which is what makes a failed send retry cleanly on the next tick.
    /// <see cref="Agent.OfflineNotifiedAt"/> is purely informational (last
    /// time this agent was actually handled) — it deliberately does not
    /// gate anything, because a freshly registered agent (never offline,
    /// <see cref="Agent.OfflineCrossed"/> still its default <c>false</c>)
    /// and an agent that just recovered and had its flag reset back to
    /// <c>false</c> are otherwise indistinguishable from a null timestamp
    /// alone — an earlier version of this method used exactly that null
    /// check as its pending-notification gate and sent a bogus "back
    /// online" email for every agent that had never gone offline at all,
    /// caught by <c>Does_not_notify_while_the_agent_is_within_the_threshold</c>.
    /// </summary>
    private async Task<bool> CheckAgentAsync(
        IServiceScope scope,
        IAuditLogService auditLog,
        Agent agent,
        DateTimeOffset utcNow,
        TimeSpan threshold,
        Agents.AgentOfflineOptions offlineOptions,
        bool canEmail,
        string? recipient,
        CancellationToken ct)
    {
        var isOfflineNow = utcNow - agent.LastAliveAt!.Value > threshold;
        if (isOfflineNow == agent.OfflineCrossed)
        {
            // Already settled in this state — nothing pending.
            return false;
        }

        var goingOffline = isOfflineNow;
        var enabled = goingOffline ? offlineOptions.OfflineNotificationEnabled : offlineOptions.OnlineRecoveryNotificationEnabled;
        var auditAction = goingOffline ? "agent.offline.detected" : "agent.offline.recovered";

        if (!enabled || !canEmail)
        {
            await auditLog.LogAsync("system", auditAction, agent.Hostname, ct);
            agent.OfflineCrossed = isOfflineNow;
            agent.OfflineNotifiedAt = utcNow;
            return true;
        }

        var (subjectEn, bodyEn, subjectDe, bodyDe) = BuildMessage(agent.Hostname, goingOffline, threshold);
        try
        {
            var email = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
            await email.SendNotificationAsync(recipient!, subjectEn, bodyEn, subjectDe, bodyDe, ct);
            await auditLog.LogAsync("system", auditAction, agent.Hostname, ct);
            agent.OfflineCrossed = isOfflineNow;
            agent.OfflineNotifiedAt = utcNow;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send the {AuditAction} notification email for {Hostname}.", auditAction, agent.Hostname);
            return false;
        }
    }

    private static (string SubjectEn, string BodyEn, string SubjectDe, string BodyDe) BuildMessage(string hostname, bool goingOffline, TimeSpan threshold)
    {
        var minutes = (int)threshold.TotalMinutes;
        return goingOffline
            ? (
                $"UpdateWatch2: {hostname} went offline",
                $"Managed machine \"{hostname}\" hasn't sent a heartbeat in over {minutes} minute(s) and is now considered offline.\n\n"
                    + "Check the UpdateWatch2 admin UI for details.",
                $"UpdateWatch2: {hostname} ist offline",
                $"Die verwaltete Maschine „{hostname}“ hat seit über {minutes} Minute(n) keinen Heartbeat gesendet und gilt jetzt als offline.\n\n"
                    + "Details findest du in der UpdateWatch2-Verwaltungsoberfläche.")
            : (
                $"UpdateWatch2: {hostname} is back online",
                $"Managed machine \"{hostname}\" is heartbeating again and is no longer considered offline.\n\n"
                    + "Check the UpdateWatch2 admin UI for details.",
                $"UpdateWatch2: {hostname} ist wieder online",
                $"Die verwaltete Maschine „{hostname}“ sendet wieder Heartbeats und gilt nicht mehr als offline.\n\n"
                    + "Details findest du in der UpdateWatch2-Verwaltungsoberfläche.");
    }
}
