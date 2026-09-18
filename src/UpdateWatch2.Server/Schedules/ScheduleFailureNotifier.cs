using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Schedules;

/// <summary>
/// The one shared "should we email about this schedule-related failure, and
/// if so how" decision point for all three places a schedule-originated
/// action can end up Failed/Missed: <see cref="ScheduleService.ExpireMissedAsync"/>
/// (a deadline-expired action), <see cref="Updates.UpdateService.AcknowledgeInstallAsync"/>
/// (a schedule-triggered install acknowledged as Failed), and
/// <see cref="Agents.AgentService.AcknowledgeRebootAsync"/> (the same for a
/// reboot) — so the three can never disagree about the guard
/// (<see cref="Db.Entities.Schedule.NotifyOnFailure"/> plus the same
/// IsConfigured-plus-recipient check every other automated notification in
/// this codebase already uses) or the audit-log/email pairing. Reuses the
/// exact bilingual <see cref="IEmailNotificationService.SendNotificationAsync"/>
/// primitive and "audit-log unconditionally regardless of whether the
/// underlying setting is on, only gate the email attempt itself" rule
/// already established elsewhere (e.g.
/// <see cref="Certificates.CertificateExpiryWorker"/>'s
/// CertificateExpiryNotificationsEnabled handling) — except here, unlike
/// that worker, there's nothing to audit-log at all when
/// <paramref name="notifyOnFailure"/> is false, since the underlying
/// Failed/Missed transition itself is already audit-logged by the caller
/// regardless (<c>schedule.run.missed</c>, or the existing install/reboot
/// acknowledgement logging) — this helper's own audit entry exists only to
/// record that an email was (or should have been, if configured) sent.
/// </summary>
public static class ScheduleFailureNotifier
{
    public static async Task NotifyAsync(
        IAdminSettingsStore settingsStore,
        IEmailNotificationService email,
        IAuditLogService auditLog,
        ILogger logger,
        string scheduleName,
        bool notifyOnFailure,
        string subject,
        string body,
        string subjectDe,
        string bodyDe,
        string auditAction,
        CancellationToken ct)
    {
        if (!notifyOnFailure)
        {
            return;
        }

        var smtp = settingsStore.Smtp;
        var recipient = smtp.NotificationRecipientAddress;
        if (!smtp.IsConfigured || string.IsNullOrWhiteSpace(recipient))
        {
            return;
        }

        try
        {
            await email.SendNotificationAsync(recipient, subject, body, subjectDe, bodyDe, ct);
            await auditLog.LogAsync("system", auditAction, scheduleName, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send the {AuditAction} notification email for schedule \"{ScheduleName}\".", auditAction, scheduleName);
        }
    }
}
