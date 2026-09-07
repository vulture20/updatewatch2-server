using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.AgentUpdates;
using UpdateWatch2.Server.Audit;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Status plus an admin-triggered manual check for the admin UI's
/// agent-auto-update section (updatewatch2-server#14) — the enabled/token
/// toggle itself lives on the existing <c>PUT /api/admin/settings</c> (see
/// <see cref="AdminController"/>), same as every other admin setting; this
/// is just the additional, non-editable "what's the newest version this
/// server currently knows about" state that setting doesn't carry, plus a
/// way to force a check right now instead of waiting for
/// <see cref="AgentUpdateCheckWorker"/>'s own interval.
/// </summary>
[ApiController]
[Route("api/admin/agent-update-status")]
[Authorize]
public class AgentUpdatesController(IAgentUpdateService agentUpdateService, IAuditLogService auditLog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await agentUpdateService.GetStatusAsync(ct));

    /// <summary>
    /// Runs the exact same check <see cref="AgentUpdateCheckWorker"/> runs
    /// on its own interval — <see cref="IAgentUpdateService.CheckForUpdatesAsync"/>
    /// is documented as safe to call more often than that interval for
    /// this exact reason (a no-op read whenever nothing changed on
    /// GitHub), so this needs no extra guarding beyond the normal admin
    /// auth this whole controller already requires. Audit-logged with the
    /// outcome so "an admin forced a check and what it found" is
    /// distinguishable from the periodic worker's own
    /// <c>agent-update.detected</c>/<c>agent-update.assets-redownloaded</c>
    /// entries.
    /// </summary>
    [HttpPost("check")]
    public async Task<IActionResult> Check(CancellationToken ct)
    {
        var outcome = await agentUpdateService.CheckForUpdatesAsync(ct);
        await auditLog.LogAsync(User.Identity!.Name!, "agent-update.manual-check", outcome.ToString(), ct);
        return Ok(await agentUpdateService.GetStatusAsync(ct));
    }
}
