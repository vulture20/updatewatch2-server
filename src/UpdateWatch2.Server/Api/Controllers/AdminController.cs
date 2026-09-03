using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Backs the "Administration" area (CLAUDE.md section 6). Currently
/// read-only; PUT endpoints to persist changes land with AD integration
/// and dynamic log-level push to agents.
/// </summary>
[ApiController]
[Route("api/admin/settings")]
public class AdminController(
    IOptionsMonitor<BruteForceOptions> bruteForce,
    IOptionsMonitor<SmtpOptions> smtp,
    IOptionsMonitor<NotificationThresholdOptions> notificationThresholds) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var bf = bruteForce.CurrentValue;
        var nt = notificationThresholds.CurrentValue;

        return Ok(new AdminSettingsDto(
            LogLevel: Environment.GetEnvironmentVariable("UPDATEWATCH2_LOGLEVEL") ?? "INFO",
            BruteForceMaxAttempts: bf.MaxAttempts,
            BruteForceWindowMinutes: bf.WindowMinutes,
            BruteForceLockoutMinutes: bf.LockoutMinutes,
            SmtpConfigured: smtp.CurrentValue.IsConfigured,
            NotificationUpdatesPerMachineThreshold: nt.UpdatesPerMachine,
            NotificationAffectedMachinesThreshold: nt.AffectedMachines));
    }
}
