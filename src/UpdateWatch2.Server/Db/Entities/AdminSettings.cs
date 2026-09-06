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

    public int NotificationUpdatesPerMachineThreshold { get; set; }

    public int NotificationAffectedMachinesThreshold { get; set; }

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

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
