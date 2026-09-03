namespace UpdateWatch2.Server.Admin;

/// <summary>
/// Full replacement of the admin-editable settings (PUT /api/admin/settings).
/// <see cref="SmtpPassword"/> and <see cref="AdBindPassword"/> are the
/// exception to "replace everything": null/omitted means "leave the
/// stored password unchanged" — GET never echoes either back, so there'd
/// be nothing for the UI to resubmit otherwise. Send an empty string to
/// clear one.
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
    int NotificationAffectedMachinesThreshold,
    bool AdEnabled,
    string AdHost,
    int AdPort,
    string AdEncryption,
    string AdBindDn,
    string? AdBindPassword,
    string AdBaseDn,
    string AdUserSearchFilter,
    string AdLoginGroupDn);
