using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>Backs the "Administration" area (CLAUDE.md section 6). Requires an admin session.</summary>
[ApiController]
[Route("api/admin/settings")]
[Authorize]
public class AdminController(IAdminSettingsStore settingsStore, IAuditLogService auditLog) : ControllerBase
{
    private static readonly string[] ValidLogLevels = ["DEBUG", "INFO", "WARNING", "ERROR"];

    // 0 is the "unlimited — never discard" sentinel (AdminSettings.AuditLogRetentionDays),
    // the rest are the fixed steps the admin UI's dropdown offers.
    private static readonly int[] ValidAuditLogRetentionDays = [0, 30, 60, 90, 180, 365];

    // 0 is the "unlimited — everything on one page" sentinel (AdminSettings.ItemsPerPage),
    // the rest are the fixed steps the admin UI's dropdown offers.
    private static readonly int[] ValidItemsPerPage = [0, 10, 25, 50, 100, 200];

    [HttpGet]
    public IActionResult Get() => Ok(settingsStore.ToDto());

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateAdminSettingsRequest request, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(new { errors });
        }

        // Normalize to the exact casing AdminSettingsStore round-trips
        // through Enum.Parse (case-sensitive) when reloading from the DB —
        // validation above only confirms these parse case-insensitively.
        var normalized = request with
        {
            LogLevel = request.LogLevel.ToUpperInvariant(),
            SmtpEncryption = Enum.Parse<SmtpEncryption>(request.SmtpEncryption, ignoreCase: true).ToString(),
            AdEncryption = Enum.Parse<AdEncryption>(request.AdEncryption, ignoreCase: true).ToString(),
        };

        var before = settingsStore.ToDto();
        var updated = await settingsStore.UpdateAsync(normalized, ct);
        await auditLog.LogAsync(User.Identity!.Name!, "admin.settings.updated", AdminSettingsDiffFormatter.Format(before, updated), ct);
        return Ok(updated);
    }

    // Every entry pairs its free-text English message (unchanged from
    // before this method returned typed items — kept byte-identical as
    // the fallback for a frontend build that doesn't recognize the code
    // yet) with a stable ApiErrorCode the web UI translates via
    // react-i18next's t() (updatewatch2-server#17). None of these need an
    // ErrorItem.Detail — the interpolated bits (ValidLogLevels,
    // Enum.GetNames<...>(), ValidAuditLogRetentionDays) are fixed constants
    // baked into both locales' translation templates, not per-request
    // dynamic content — see ApiErrorCode's own doc comment for what
    // actually needs Detail instead.
    private static List<ApiErrorItem> Validate(UpdateAdminSettingsRequest request)
    {
        var errors = new List<ApiErrorItem>();

        if (!ValidLogLevels.Contains(request.LogLevel.ToUpperInvariant()))
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.LogLevelInvalid, $"LogLevel must be one of: {string.Join(", ", ValidLogLevels)}."));
        }

        if (request.BruteForceMaxAttempts < 1)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.BruteForceMaxAttemptsInvalid, "BruteForceMaxAttempts must be at least 1."));
        }

        if (request.BruteForceWindowMinutes < 1)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.BruteForceWindowMinutesInvalid, "BruteForceWindowMinutes must be at least 1."));
        }

        if (request.BruteForceLockoutMinutes < 1)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.BruteForceLockoutMinutesInvalid, "BruteForceLockoutMinutes must be at least 1."));
        }

        if (request.SmtpPort is < 1 or > 65535)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.SmtpPortInvalid, "SmtpPort must be between 1 and 65535."));
        }

        if (!Enum.TryParse<SmtpEncryption>(request.SmtpEncryption, ignoreCase: true, out _))
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.SmtpEncryptionInvalid, $"SmtpEncryption must be one of: {string.Join(", ", Enum.GetNames<SmtpEncryption>())}."));
        }

        if (request.NotificationUpdatesPerMachineThreshold < 1)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.NotificationUpdatesPerMachineThresholdInvalid, "NotificationUpdatesPerMachineThreshold must be at least 1."));
        }

        if (request.NotificationAffectedMachinesThreshold < 1)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.NotificationAffectedMachinesThresholdInvalid, "NotificationAffectedMachinesThreshold must be at least 1."));
        }

        if (!Enum.TryParse<AdEncryption>(request.AdEncryption, ignoreCase: true, out _))
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.AdEncryptionInvalid, $"AdEncryption must be one of: {string.Join(", ", Enum.GetNames<AdEncryption>())}."));
        }

        if (request.AdPort is < 1 or > 65535)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.AdPortInvalid, "AdPort must be between 1 and 65535."));
        }

        if (request.AdEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.AdHost))
            {
                errors.Add(new ApiErrorItem(ApiErrorCode.AdHostRequired, "AdHost is required when AD login is enabled."));
            }

            if (string.IsNullOrWhiteSpace(request.AdBaseDn))
            {
                errors.Add(new ApiErrorItem(ApiErrorCode.AdBaseDnRequired, "AdBaseDn is required when AD login is enabled."));
            }

            if (string.IsNullOrWhiteSpace(request.AdUserSearchFilter))
            {
                errors.Add(new ApiErrorItem(ApiErrorCode.AdUserSearchFilterRequired, "AdUserSearchFilter is required when AD login is enabled."));
            }
            else if (!request.AdUserSearchFilter.Contains("{0}"))
            {
                errors.Add(new ApiErrorItem(ApiErrorCode.AdUserSearchFilterMissingPlaceholder, "AdUserSearchFilter must contain a {0} placeholder for the submitted username."));
            }

            if (string.IsNullOrWhiteSpace(request.AdLoginGroupDn))
            {
                errors.Add(new ApiErrorItem(ApiErrorCode.AdLoginGroupDnRequired, "AdLoginGroupDn is required when AD login is enabled."));
            }
        }

        // Upper bound matches the CA root's fixed 10-year validity — an
        // agent certificate that outlives the root chaining to it is
        // nonsensical, not just unusual.
        if (request.AgentCertificateValidityDays is < 1 or > 3650)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.AgentCertificateValidityDaysInvalid, "AgentCertificateValidityDays must be between 1 and 3650."));
        }

        if (request.AgentAutoUpdateCheckIntervalHours < 1)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.AgentAutoUpdateCheckIntervalHoursInvalid, "AgentAutoUpdateCheckIntervalHours must be at least 1."));
        }

        if (!ValidAuditLogRetentionDays.Contains(request.AuditLogRetentionDays))
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.AuditLogRetentionDaysInvalid, $"AuditLogRetentionDays must be one of: {string.Join(", ", ValidAuditLogRetentionDays)} (0 = unlimited)."));
        }

        // Upper bound is arbitrary but generous — a year's notice is more
        // than any admin plausibly needs, and it keeps this in the same
        // free-form-but-bounded style as AgentCertificateValidityDays above.
        if (request.CertificateExpiryWarningLeadDays is < 1 or > 365)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.CertificateExpiryWarningLeadDaysInvalid, "CertificateExpiryWarningLeadDays must be between 1 and 365."));
        }

        if (!string.IsNullOrWhiteSpace(request.NotificationRecipientAddress) && !request.NotificationRecipientAddress.Contains('@'))
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.NotificationRecipientAddressInvalid, "NotificationRecipientAddress must be a valid email address."));
        }

        if (!string.IsNullOrWhiteSpace(request.InstanceUrl)
            && (!Uri.TryCreate(request.InstanceUrl, UriKind.Absolute, out var instanceUri) || instanceUri.Scheme is not ("http" or "https")))
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.InstanceUrlInvalid, "InstanceUrl must be an absolute http:// or https:// URL."));
        }

        // Upper bound generous (a day) — same free-form-but-bounded style
        // as the other admin-configurable interval-like settings above.
        if (request.AgentOfflineThresholdMinutes is < 1 or > 1440)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.AgentOfflineThresholdMinutesInvalid, "AgentOfflineThresholdMinutes must be between 1 and 1440."));
        }

        // Validated the same way ScheduleRecurrenceCalculator itself will
        // eventually resolve it (TimeZoneInfo.FindSystemTimeZoneById) so a
        // value that passes here is guaranteed to actually work later —
        // .NET on Linux resolves IANA IDs directly (no Windows-ID mapping
        // needed, since this project only ever runs the server on Linux).
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.TimeZoneIdInvalid, "TimeZoneId must be a valid IANA time zone identifier (e.g. \"Europe/Berlin\")."));
        }

        if (!ValidItemsPerPage.Contains(request.ItemsPerPage))
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.ItemsPerPageInvalid, $"ItemsPerPage must be one of: {string.Join(", ", ValidItemsPerPage)} (0 = unlimited)."));
        }

        return errors;
    }
}
