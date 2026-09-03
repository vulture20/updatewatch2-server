namespace UpdateWatch2.Server.Admin;

/// <summary>
/// Full replacement of the admin-editable settings (PUT /api/admin/settings).
/// <see cref="SmtpPassword"/> is the one exception to "replace everything":
/// null/omitted means "leave the stored password unchanged" — GET never
/// echoes it back, so there'd be nothing for the UI to resubmit otherwise.
/// Send an empty string to clear it.
/// </summary>
public record UpdateAdminSettingsRequest(
    string LogLevel,
    int BruteForceMaxAttempts,
    int BruteForceWindowMinutes,
    int BruteForceLockoutMinutes,
    string SmtpHost,
    int SmtpPort,
    string? SmtpUsername,
    string? SmtpPassword,
    string SmtpEncryption,
    string SmtpFromAddress,
    string SmtpFromName,
    int NotificationUpdatesPerMachineThreshold,
    int NotificationAffectedMachinesThreshold);
