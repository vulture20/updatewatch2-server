namespace UpdateWatch2.Server.AgentUpdates;

/// <summary>
/// Admin-configurable agent-auto-update settings (updatewatch2-server#14).
/// Bound from appsettings.json's "AgentAutoUpdate" section only as the
/// compiled-in default <see cref="Admin.AdminSettingsStore"/> seeds its DB
/// row from on first run — the database is authoritative after that, the
/// same convention every other admin-editable options group in this
/// codebase already follows (<see cref="Auth.BruteForceOptions"/>,
/// <see cref="Notifications.SmtpOptions"/>, ...).
/// </summary>
public class AgentAutoUpdateOptions
{
    public const string SectionName = "AgentAutoUpdate";

    public bool Enabled { get; set; } = true;

    /// <summary>Optional GitHub personal access token — see <c>AdminSettings.GitHubToken</c>'s doc comment for why.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>
    /// How often <see cref="AgentUpdateCheckWorker"/> checks GitHub for a
    /// newer release. Read fresh on every loop iteration (not cached at
    /// startup), so an admin shortening or lengthening it takes effect on
    /// the very next check, the same live-reload behavior every other
    /// admin setting already gets — see <see cref="AgentUpdateCheckWorker"/>'s
    /// own doc comment. Matches this feature's original hardcoded default
    /// of 6 hours.
    /// </summary>
    public int CheckIntervalHours { get; set; } = 6;
}
