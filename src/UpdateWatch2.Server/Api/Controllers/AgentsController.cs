using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Updates;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Agent overview, detail view, and onboarding approval. Requires an admin
/// session (see AuthController). Agent self-registration isn't implemented
/// yet — that will be a separate, mutual-TLS-authenticated endpoint, see
/// updatewatch2-server#3, not a route on this admin-facing controller.
/// Bulk actions from the overview list's multi-select (approve/delete/
/// install/reboot) all live here as sibling <c>POST api/agents/&lt;verb&gt;</c>
/// routes, alongside their existing single-agent <c>{hostname}/&lt;verb&gt;</c>
/// counterparts — <see cref="IUpdateService"/> is injected in addition to
/// <see cref="IAgentService"/> purely for <see cref="InstallMany"/>, since
/// install itself (single or bulk) is otherwise owned by UpdatesController/
/// IUpdateService, not this controller.
/// </summary>
[ApiController]
[Route("api/agents")]
[Authorize]
public class AgentsController(IAgentService agentService, IUpdateService updateService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await agentService.GetAllAsync(ct));

    [HttpGet("{hostname}")]
    public async Task<IActionResult> GetByHostname(string hostname, CancellationToken ct)
    {
        var agent = await agentService.GetByHostnameAsync(hostname, ct);
        return agent is null ? NotFound() : Ok(agent);
    }

    [HttpPost("{hostname}/approve")]
    public async Task<IActionResult> Approve(string hostname, CancellationToken ct)
    {
        var approved = await agentService.ApproveAsync(hostname, approvedBy: User.Identity!.Name!, ct);
        return approved ? NoContent() : NotFound();
    }

    [HttpPost("approve")]
    public async Task<IActionResult> ApproveMany([FromBody] BulkApproveRequest request, CancellationToken ct)
    {
        var result = await agentService.ApproveManyAsync(request.Hostnames, approvedBy: User.Identity!.Name!, ct);
        return Ok(result);
    }

    // Admin-mediated recovery for a lost/wiped agent certificate
    // (updatewatch2-server#8) — the returned token is shown exactly once,
    // never retrievable again, for the admin to place into the agent's
    // local configuration.
    [HttpPost("{hostname}/reissue-certificate")]
    public async Task<IActionResult> ReissueCertificate(string hostname, CancellationToken ct)
    {
        var result = await agentService.ReissueCertificateAsync(hostname, initiatedBy: User.Identity!.Name!, ct);
        if (result.Success)
        {
            return Ok(new { registrationToken = result.RegistrationToken });
        }

        return result.FailureReason == "Agent not found."
            ? NotFound()
            : Conflict(new { message = result.FailureReason, errorCode = result.ErrorCode });
    }

    // Fire-and-forget, like ApproveAsync — actual delivery happens on the
    // agent's own next alive heartbeat (see IAgentService.TriggerRebootAsync).
    // Reboots the agent's own machine — not just its service process, and
    // never the OS-update install pipeline (CLAUDE.md's "the admin decides
    // when to actually trigger a reboot" rule).
    [HttpPost("{hostname}/reboot")]
    public async Task<IActionResult> Reboot(string hostname, CancellationToken ct)
    {
        var found = await agentService.TriggerRebootAsync(hostname, triggeredBy: User.Identity!.Name!, ct);
        return found ? Accepted() : NotFound();
    }

    // Permanent — see IAgentService.DeleteAsync's doc comment for why this
    // is effective immediately (no separate certificate-revocation step
    // needed) and what happens if the same hostname registers again later.
    [HttpDelete("{hostname}")]
    public async Task<IActionResult> Delete(string hostname, CancellationToken ct)
    {
        var deleted = await agentService.DeleteAsync(hostname, initiatedBy: User.Identity!.Name!, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("delete")]
    public async Task<IActionResult> DeleteMany([FromBody] BulkDeleteRequest request, CancellationToken ct)
    {
        var result = await agentService.DeleteManyAsync(request.Hostnames, initiatedBy: User.Identity!.Name!, ct);
        return Ok(result);
    }

    // Delegates to IUpdateService, not IAgentService — see this
    // controller's own doc comment for why.
    [HttpPost("install")]
    public async Task<IActionResult> InstallMany([FromBody] BulkInstallRequest request, CancellationToken ct)
    {
        var result = await updateService.TriggerInstallManyAsync(request.Hostnames, triggeredBy: User.Identity!.Name!, ct);
        return Ok(result);
    }

    [HttpPost("reboot")]
    public async Task<IActionResult> RebootMany([FromBody] BulkRebootRequest request, CancellationToken ct)
    {
        var result = await agentService.TriggerRebootManyAsync(request.Hostnames, triggeredBy: User.Identity!.Name!, ct);
        return Ok(result);
    }

    // Full replace, not a partial merge — see UpdateAgentSettingsRequest's
    // own doc comment. Delivery is the agent's own alive-heartbeat poll,
    // exactly like Reboot/InstallMany above — no separate ack needed, see
    // IAgentService.UpdateSettingsAsync's doc comment for why.
    [HttpPut("{hostname}/settings")]
    public async Task<IActionResult> UpdateSettings(string hostname, [FromBody] UpdateAgentSettingsRequest request, CancellationToken ct)
    {
        if (!AgentSettingsValidator.IsValid(request))
        {
            return BadRequest(new { message = "Invalid agent settings." });
        }

        var found = await agentService.UpdateSettingsAsync(
            hostname, initiatedBy: User.Identity!.Name!, request.DesiredLogLevel, request.DesiredUpdateCheckIntervalMinutes,
            request.DesiredUpdateCheckJitterSeconds, request.DesiredAliveIntervalMinutes, ct);
        return found ? NoContent() : NotFound();
    }

    // Bulk counterpart from the overview list's multi-select — see
    // BulkUpdateAgentSettingsRequest's own doc comment for why this is a
    // per-field opt-in (nullable fields) rather than the single-agent
    // route's always-full-replace shape.
    [HttpPost("settings")]
    public async Task<IActionResult> UpdateSettingsMany([FromBody] BulkUpdateAgentSettingsRequest request, CancellationToken ct)
    {
        if (!AgentSettingsValidator.IsValidBulkRequest(request))
        {
            return BadRequest(new { message = "Invalid agent settings." });
        }

        var result = await agentService.UpdateSettingsManyAsync(
            request.Hostnames, initiatedBy: User.Identity!.Name!, request.DesiredLogLevel, request.DesiredUpdateCheckIntervalMinutes,
            request.DesiredUpdateCheckJitterSeconds, request.DesiredAliveIntervalMinutes, ct);
        return Ok(result);
    }
}
