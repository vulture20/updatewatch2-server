namespace UpdateWatch2.Server.Admin;

/// <summary>Read-only snapshot of the settings shown under Administration. Update endpoints land with the Admin module.</summary>
public record AdminSettingsDto(
    string LogLevel,
    int BruteForceMaxAttempts,
    int BruteForceWindowMinutes,
    int BruteForceLockoutMinutes,
    bool SmtpConfigured,
    int NotificationUpdatesPerMachineThreshold,
    int NotificationAffectedMachinesThreshold);
