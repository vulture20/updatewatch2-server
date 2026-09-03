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

        var updated = await settingsStore.UpdateAsync(normalized, ct);
        await auditLog.LogAsync(User.Identity!.Name!, "admin.settings.updated", ct: ct);
        return Ok(updated);
    }

    private static List<string> Validate(UpdateAdminSettingsRequest request)
    {
        var errors = new List<string>();

        if (!ValidLogLevels.Contains(request.LogLevel.ToUpperInvariant()))
        {
            errors.Add($"LogLevel must be one of: {string.Join(", ", ValidLogLevels)}.");
        }

        if (request.BruteForceMaxAttempts < 1)
        {
            errors.Add("BruteForceMaxAttempts must be at least 1.");
        }

        if (request.BruteForceWindowMinutes < 1)
        {
            errors.Add("BruteForceWindowMinutes must be at least 1.");
        }

        if (request.BruteForceLockoutMinutes < 1)
        {
            errors.Add("BruteForceLockoutMinutes must be at least 1.");
        }

        if (request.SmtpPort is < 1 or > 65535)
        {
            errors.Add("SmtpPort must be between 1 and 65535.");
        }

        if (!Enum.TryParse<SmtpEncryption>(request.SmtpEncryption, ignoreCase: true, out _))
        {
            errors.Add($"SmtpEncryption must be one of: {string.Join(", ", Enum.GetNames<SmtpEncryption>())}.");
        }

        if (request.NotificationUpdatesPerMachineThreshold < 1)
        {
            errors.Add("NotificationUpdatesPerMachineThreshold must be at least 1.");
        }

        if (request.NotificationAffectedMachinesThreshold < 1)
        {
            errors.Add("NotificationAffectedMachinesThreshold must be at least 1.");
        }

        if (!Enum.TryParse<AdEncryption>(request.AdEncryption, ignoreCase: true, out _))
        {
            errors.Add($"AdEncryption must be one of: {string.Join(", ", Enum.GetNames<AdEncryption>())}.");
        }

        if (request.AdPort is < 1 or > 65535)
        {
            errors.Add("AdPort must be between 1 and 65535.");
        }

        if (request.AdEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.AdHost))
            {
                errors.Add("AdHost is required when AD login is enabled.");
            }

            if (string.IsNullOrWhiteSpace(request.AdBaseDn))
            {
                errors.Add("AdBaseDn is required when AD login is enabled.");
            }

            if (string.IsNullOrWhiteSpace(request.AdUserSearchFilter))
            {
                errors.Add("AdUserSearchFilter is required when AD login is enabled.");
            }
            else if (!request.AdUserSearchFilter.Contains("{0}"))
            {
                errors.Add("AdUserSearchFilter must contain a {0} placeholder for the submitted username.");
            }

            if (string.IsNullOrWhiteSpace(request.AdLoginGroupDn))
            {
                errors.Add("AdLoginGroupDn is required when AD login is enabled.");
            }
        }

        return errors;
    }
}
