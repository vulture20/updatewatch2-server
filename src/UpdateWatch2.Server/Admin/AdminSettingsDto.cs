namespace UpdateWatch2.Server.Admin;

/// <summary>
/// The settings shown (and, via PUT, editable) under Administration. Never
/// includes the SMTP or AD bind passwords — <see cref="SmtpPasswordSet"/>/
/// <see cref="AdBindPasswordSet"/> only say whether one is stored.
/// </summary>
public record AdminSettingsDto(
    string LogLevel,
    int BruteForceMaxAttempts,
    int BruteForceWindowMinutes,
    int BruteForceLockoutMinutes,
    string SmtpHost,
    int SmtpPort,
    string? SmtpUsername,
    bool SmtpPasswordSet,
    string SmtpEncryption,
    string SmtpFromAddress,
    string SmtpFromName,
    bool SmtpConfigured,
    int NotificationUpdatesPerMachineThreshold,
    int NotificationAffectedMachinesThreshold,
    bool AdEnabled,
    string AdHost,
    int AdPort,
    string AdEncryption,
    string AdBindDn,
    bool AdBindPasswordSet,
    string AdBaseDn,
    string AdUserSearchFilter,
    string AdLoginGroupDn,
    bool AdConfigured,
    int AgentCertificateValidityDays,
    bool AgentAutoUpdateEnabled,
    bool GitHubTokenSet,
    int AgentAutoUpdateCheckIntervalHours);
