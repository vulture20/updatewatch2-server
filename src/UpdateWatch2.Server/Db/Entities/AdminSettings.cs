namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// The single, persisted row of admin-editable settings (CLAUDE.md section
/// 6). Seeded once from appsettings.json's compiled-in defaults on first
/// run (see <see cref="Admin.AdminSettingsStore"/>); the database is
/// authoritative from then on, updated only via the Administration UI's
/// PUT endpoint.
/// </summary>
public class AdminSettings
{
    public int Id { get; set; }

    public required string LogLevel { get; set; }

    public int BruteForceMaxAttempts { get; set; }

    public int BruteForceWindowMinutes { get; set; }

    public int BruteForceLockoutMinutes { get; set; }

    public required string SmtpHost { get; set; }

    public int SmtpPort { get; set; }

    public string? SmtpUsername { get; set; }

    public string? SmtpPassword { get; set; }

    public required string SmtpEncryption { get; set; }

    public required string SmtpFromAddress { get; set; }

    public required string SmtpFromName { get; set; }

    /// <summary>Where automated alert emails go — see <see cref="Notifications.SmtpOptions.NotificationRecipientAddress"/>'s doc comment.</summary>
    public string? NotificationRecipientAddress { get; set; }

    /// <summary>The instance link added to every notification email — see <see cref="Notifications.SmtpOptions.InstanceUrl"/>'s doc comment.</summary>
    public string? InstanceUrl { get; set; }

    public int NotificationUpdatesPerMachineThreshold { get; set; }

    /// <summary>Independent on/off checkbox for the updates-per-machine half of the threshold notification (see <see cref="Notifications.NotificationThresholdOptions.UpdatesPerMachineEnabled"/>'s doc comment). Default true.</summary>
    public bool NotificationUpdatesPerMachineEnabled { get; set; } = true;

    public int NotificationAffectedMachinesThreshold { get; set; }

    /// <summary>Independent on/off checkbox for the affected-machines half of the threshold notification (see <see cref="Notifications.NotificationThresholdOptions.AffectedMachinesEnabled"/>'s doc comment). Default true.</summary>
    public bool NotificationAffectedMachinesEnabled { get; set; } = true;

    public bool AdEnabled { get; set; }

    public required string AdHost { get; set; }

    public int AdPort { get; set; }

    public required string AdEncryption { get; set; }

    public required string AdBindDn { get; set; }

    public string? AdBindPassword { get; set; }

    public required string AdBaseDn { get; set; }

    public required string AdUserSearchFilter { get; set; }

    public required string AdLoginGroupDn { get; set; }

    /// <summary>Days a newly issued/renewed agent client certificate stays valid (updatewatch2-server#9). Not retroactive — applies to future issuance only.</summary>
    public int AgentCertificateValidityDays { get; set; }

    /// <summary>
    /// Admin-facing on/off toggle for checking GitHub for new agent
    /// releases and distributing them to agents (updatewatch2-server#14).
    /// Default true. The env var <c>UPDATEWATCH2_AUTOUPDATE=false</c> is a
    /// separate, higher-priority master kill switch that overrides this —
    /// see <see cref="AgentUpdates.IAgentUpdateService.IsEnabled"/> —
    /// rather than a value stored here, since it must win even without a
    /// database write (matching <c>UPDATEWATCH2_DEMOMODE</c>/
    /// <c>UPDATEWATCH2_TRUSTEDIP</c>'s existing env-var-wins-outright pattern).
    /// </summary>
    public bool AgentAutoUpdateEnabled { get; set; } = true;

    /// <summary>
    /// Optional GitHub personal access token, raising the otherwise-tight
    /// 60-requests/hour anonymous GitHub API rate limit to 5,000/hour.
    /// Stored in plaintext like <see cref="SmtpPassword"/>/<see cref="AdBindPassword"/>
    /// (never echoed back through <see cref="Admin.AdminSettingsDto"/> —
    /// see <see cref="Admin.AdminSettingsDto.GitHubTokenSet"/> instead).
    /// </summary>
    public string? GitHubToken { get; set; }

    /// <summary>How often (in hours) to check GitHub for a newer agent release — see <see cref="AgentUpdates.AgentAutoUpdateOptions.CheckIntervalHours"/>.</summary>
    public int AgentAutoUpdateCheckIntervalHours { get; set; } = 6;

    /// <summary>
    /// How many days of audit log entries to keep before a periodic
    /// cleanup permanently discards them (see <see cref="Audit.AuditLogRetentionWorker"/>).
    /// Admin-configurable via a fixed set of steps in the UI (30/60/90/180/365),
    /// plus <c>0</c> as the sentinel for "unlimited — never discard"; the
    /// API itself doesn't reject an out-of-set positive value, matching
    /// how every other free-form numeric setting here is only clamped, not
    /// enum-constrained. Default 90 days.
    /// </summary>
    public int AuditLogRetentionDays { get; set; } = 90;

    /// <summary>See <see cref="Certificates.CertificateOptions.CertificateExpiryWarningLeadDays"/>'s doc comment. Default 60.</summary>
    public int CertificateExpiryWarningLeadDays { get; set; } = 60;

    /// <summary>See <see cref="Certificates.CertificateOptions.CertificateExpiryNotificationsEnabled"/>'s doc comment. Default true.</summary>
    public bool CertificateExpiryNotificationsEnabled { get; set; } = true;

    /// <summary>See <see cref="Agents.AgentOfflineOptions.ThresholdMinutes"/>'s doc comment. Default 15.</summary>
    public int AgentOfflineThresholdMinutes { get; set; } = 15;

    /// <summary>See <see cref="Agents.AgentOfflineOptions.OfflineNotificationEnabled"/>'s doc comment. Default true.</summary>
    public bool AgentOfflineNotificationEnabled { get; set; } = true;

    /// <summary>See <see cref="Agents.AgentOfflineOptions.OnlineRecoveryNotificationEnabled"/>'s doc comment. Default true.</summary>
    public bool AgentOnlineRecoveryNotificationEnabled { get; set; } = true;

    /// <summary>See <see cref="Admin.IAdminSettingsStore.PreDownloadWindowsUpdatesEnabled"/>'s doc comment. Default true.</summary>
    public bool PreDownloadWindowsUpdatesEnabled { get; set; } = true;

    /// <summary>See <see cref="Admin.IAdminSettingsStore.PreDownloadLinuxUpdatesEnabled"/>'s doc comment. Default true.</summary>
    public bool PreDownloadLinuxUpdatesEnabled { get; set; } = true;

    /// <summary>
    /// The IANA time zone ID (e.g. "Europe/Berlin") <see cref="Schedules.ScheduleRecurrenceCalculator"/>
    /// uses for a Recurring/Cron schedule's bare time-of-day/cron
    /// expression — see that class's own doc comment for the confusing
    /// bug this setting replaced (relying on the server process's own OS
    /// time zone, which defaults to UTC in a container unless a `TZ`
    /// environment variable is set, easy to forget). Default "UTC" — the
    /// same honest "not configured yet" default this codebase already uses
    /// elsewhere (e.g. <see cref="NotificationRecipientAddress"/>) rather
    /// than guessing at seed time, since the server process's own OS zone
    /// at that moment is exactly the unreliable signal this setting exists
    /// to stop depending on.
    /// </summary>
    public required string TimeZoneId { get; set; }

    /// <summary>
    /// How many rows the paginated admin-UI lists (agent overview,
    /// schedules, audit log) show per page. Admin-configurable via a fixed
    /// set of steps (10/25/50/100/200), plus <c>0</c> as the sentinel for
    /// "unlimited — everything on one page/response", the same convention
    /// <see cref="AuditLogRetentionDays"/> already uses for its own
    /// unlimited option. One global value shared by all three lists, not a
    /// per-list override. Default 50.
    /// </summary>
    public int ItemsPerPage { get; set; } = 50;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
