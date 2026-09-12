using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.UpdateFilters;

namespace UpdateWatch2.Server.Notifications;

/// <summary>
/// Periodically evaluates the two independent, OR-combined update-notification
/// thresholds CLAUDE.md describes ("either one crossing its threshold fires
/// the notification") — updatewatch2-server#18, closing the gap CLAUDE.md
/// itself long flagged as "not yet implemented". This project's fifth
/// server-side <see cref="BackgroundService"/>, after
/// <see cref="AgentUpdates.AgentUpdateCheckWorker"/>/<see cref="Audit.AuditLogRetentionWorker"/>/
/// <see cref="Certificates.CertificateExpiryWorker"/>/<see cref="Db.DatabaseVacuumWorker"/> —
/// same shape as all four: a check immediately on startup, then every
/// <see cref="_checkInterval"/>, re-reading the live admin-configured
/// thresholds/toggles on every tick.
///
/// The two conditions are:
/// <list type="bullet">
/// <item><b>Updates-per-machine</b>: the single worst-affected agent's own
/// pending-update count (after <see cref="UpdateFilterMatcher"/> exclusion,
/// the same shared decision point <see cref="Updates.UpdateService"/> and
/// <see cref="Agents.AgentService"/> already use, so this worker can never
/// disagree with what the admin UI itself displays) reaches
/// <see cref="NotificationThresholdOptions.UpdatesPerMachine"/>.</item>
/// <item><b>Affected machines</b>: the number of agents with at least one
/// non-filtered pending update reaches
/// <see cref="NotificationThresholdOptions.AffectedMachines"/>.</item>
/// </list>
///
/// Each has its own independent admin-facing checkbox
/// (<see cref="NotificationThresholdOptions.UpdatesPerMachineEnabled"/>/
/// <see cref="NotificationThresholdOptions.AffectedMachinesEnabled"/>, both
/// default true, at the user's explicit request) — a disabled condition is
/// simply never evaluated as crossed, independent of the other one and of
/// whether SMTP/a recipient is even configured.
///
/// Unlike <see cref="Certificates.CertificateNotificationState"/>'s
/// thumbprint-keyed "which generation was already warned about" tracking, a
/// threshold condition has no natural identity to key on, so
/// <see cref="UpdateThresholdNotificationState"/> tracks each edge-triggered
/// instead: a notification fires once when a condition transitions from
/// not-crossed to crossed, never again on a later tick while it stays
/// crossed, and only fires again once the count has genuinely dropped back
/// below the threshold and later crosses it again. Both conditions follow
/// the same "audit-log and mark handled only once actually handled" rule
/// <see cref="Certificates.CertificateExpiryWorker"/> already established:
/// with no recipient configured, "handled" means immediately (nothing to
/// retry); with one configured, "handled" means the email actually sent —
/// a transient SMTP failure leaves the state untouched so the identical
/// audit-log-plus-email attempt retries whole on the next tick.
/// </summary>
public class UpdateThresholdNotificationWorker(
    IServiceScopeFactory scopeFactory,
    IAdminSettingsStore settingsStore,
    ILogger<UpdateThresholdNotificationWorker> logger,
    TimeSpan? checkInterval = null) : BackgroundService
{
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromHours(1);

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
                logger.LogError(ex, "Update-threshold notification check failed unexpectedly.");
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
        var thresholds = settingsStore.NotificationThresholds;
        var smtp = settingsStore.Smtp;
        var recipient = smtp.NotificationRecipientAddress;
        var canEmail = smtp.IsConfigured && !string.IsNullOrWhiteSpace(recipient);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var filters = await db.UpdateFilters.ToListAsync(ct);
        var items = await db.UpdateItems.Select(u => new { u.AgentId, u.Title }).ToListAsync(ct);
        var countsByAgent = items
            .Where(u => !UpdateFilterMatcher.IsExcluded(u.Title, filters))
            .GroupBy(u => u.AgentId)
            .ToDictionary(g => g.Key, g => g.Count());

        var worstMachineCount = countsByAgent.Count == 0 ? 0 : countsByAgent.Values.Max();
        var affectedMachineCount = countsByAgent.Count;

        var state = await db.UpdateThresholdNotificationStates.SingleOrDefaultAsync(ct);
        if (state is null)
        {
            state = new UpdateThresholdNotificationState();
            db.UpdateThresholdNotificationStates.Add(state);
        }

        var stateChanged = false;
        stateChanged |= await CheckConditionAsync(
            scope, auditLog, state,
            enabled: thresholds.UpdatesPerMachineEnabled,
            isCrossedNow: thresholds.UpdatesPerMachineEnabled && worstMachineCount >= thresholds.UpdatesPerMachine,
            getCrossed: () => state.UpdatesPerMachineCrossed,
            setCrossed: v => state.UpdatesPerMachineCrossed = v,
            getNotifiedAt: () => state.UpdatesPerMachineNotifiedAt,
            setNotifiedAt: v => state.UpdatesPerMachineNotifiedAt = v,
            auditAction: "notifications.updates-per-machine-threshold.crossed",
            subject: "UpdateWatch2: updates-per-machine threshold exceeded",
            body: $"At least one managed machine currently has {worstMachineCount} pending update(s), "
                + $"meeting or exceeding the configured threshold of {thresholds.UpdatesPerMachine}.\n\n"
                + "Check the UpdateWatch2 admin UI for details.",
            details: $"worst-affected machine has {worstMachineCount} pending update(s), threshold {thresholds.UpdatesPerMachine}",
            canEmail, recipient, ct);

        stateChanged |= await CheckConditionAsync(
            scope, auditLog, state,
            enabled: thresholds.AffectedMachinesEnabled,
            isCrossedNow: thresholds.AffectedMachinesEnabled && affectedMachineCount >= thresholds.AffectedMachines,
            getCrossed: () => state.AffectedMachinesCrossed,
            setCrossed: v => state.AffectedMachinesCrossed = v,
            getNotifiedAt: () => state.AffectedMachinesNotifiedAt,
            setNotifiedAt: v => state.AffectedMachinesNotifiedAt = v,
            auditAction: "notifications.affected-machines-threshold.crossed",
            subject: "UpdateWatch2: affected-machines threshold exceeded",
            body: $"{affectedMachineCount} managed machine(s) currently have at least one pending update, "
                + $"meeting or exceeding the configured threshold of {thresholds.AffectedMachines}.\n\n"
                + "Check the UpdateWatch2 admin UI for details.",
            details: $"{affectedMachineCount} affected machine(s), threshold {thresholds.AffectedMachines}",
            canEmail, recipient, ct);

        if (stateChanged)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// The shared edge-triggered check/notify logic both thresholds use —
    /// see this class's own doc comment for the reasoning. Returns true if
    /// any field on <paramref name="state"/> was changed and needs saving.
    /// </summary>
    private async Task<bool> CheckConditionAsync(
        IServiceScope scope,
        IAuditLogService auditLog,
        UpdateThresholdNotificationState state,
        bool enabled,
        bool isCrossedNow,
        Func<bool> getCrossed,
        Action<bool> setCrossed,
        Func<DateTimeOffset?> getNotifiedAt,
        Action<DateTimeOffset?> setNotifiedAt,
        string auditAction,
        string subject,
        string body,
        string details,
        bool canEmail,
        string? recipient,
        CancellationToken ct)
    {
        var changed = false;

        if (isCrossedNow && !getCrossed())
        {
            setCrossed(true);
            setNotifiedAt(null);
            changed = true;
        }
        else if (!isCrossedNow && getCrossed())
        {
            // Dropped back below the threshold (or the condition was
            // disabled) — clears the way for a fresh notification the next
            // time it's crossed again, rather than staying permanently
            // "already notified" for one specific episode.
            setCrossed(false);
            setNotifiedAt(null);
            changed = true;
        }

        if (!enabled || !getCrossed() || getNotifiedAt() is not null)
        {
            return changed;
        }

        if (!canEmail)
        {
            await auditLog.LogAsync("system", auditAction, $"{details} (no notification recipient configured)", ct);
            setNotifiedAt(DateTimeOffset.UtcNow);
            return true;
        }

        try
        {
            var email = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
            await email.SendNotificationAsync(recipient!, subject, body, ct);
            await auditLog.LogAsync("system", auditAction, details, ct);
            setNotifiedAt(DateTimeOffset.UtcNow);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send the {AuditAction} notification email.", auditAction);
            return changed;
        }
    }
}
