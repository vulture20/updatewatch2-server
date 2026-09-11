using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Updates;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Per-agent update reporting (agent → server) and remote install trigger
/// (admin → server → agent). See CLAUDE.md sections 2.1 and 2.2.
/// </summary>
[ApiController]
[Route("api/agents/{hostname}")]
public class UpdatesController(IUpdateService updateService) : ControllerBase
{
    [HttpGet("updates")]
    [Authorize]
    public async Task<IActionResult> GetUpdates(string hostname, CancellationToken ct)
    {
        var updates = await updateService.GetForAgentAsync(hostname, ct);
        return updates is null ? NotFound() : Ok(updates);
    }

    // Not admin-facing: this is the agent's own self-report, so it doesn't
    // require the admin cookie session — it requires the agent's own
    // mutual-TLS client certificate instead (updatewatch2-server#1).
    [HttpPost("updates")]
    [Authorize(Policy = CertificateAuthenticationSetup.AgentCertificatePolicy)]
    public async Task<IActionResult> ReportUpdates(string hostname, [FromBody] ReportUpdatesRequest request, CancellationToken ct)
    {
        // Defense in depth: a validly-approved agent for host A must not be
        // able to tamper with the URL and post as host B.
        if (!string.Equals(User.Identity?.Name, hostname, StringComparison.Ordinal))
        {
            return Forbid();
        }

        var found = await updateService.ReportUpdatesAsync(hostname, request, ct);
        return found ? NoContent() : NotFound();
    }

    // request is nullable, and its own UpdateItemIds field is nullable too
    // — a bare POST with no body (the original behavior, before an admin
    // could spare specific updates) still installs everything pending.
    [HttpPost("install")]
    [Authorize]
    public async Task<IActionResult> TriggerInstall(string hostname, [FromBody] TriggerInstallRequest? request, CancellationToken ct)
    {
        var found = await updateService.TriggerInstallAsync(hostname, triggeredBy: User.Identity!.Name!, request?.UpdateItemIds, ct);
        return found ? Accepted() : NotFound();
    }

    // Agent-facing, not admin-facing (mTLS, like ReportUpdates above) — the
    // agent's acknowledgement that it acted on a pending install request
    // delivered via its alive heartbeat (updatewatch2-server#10/
    // updatewatch2-agent#4). See AgentProtocolController.Alive for delivery
    // and IUpdateService.AcknowledgeInstallAsync for what this clears.
    [HttpPost("install-ack")]
    [Authorize(Policy = CertificateAuthenticationSetup.AgentCertificatePolicy)]
    public async Task<IActionResult> AcknowledgeInstall(string hostname, [FromBody] InstallAckRequest request, CancellationToken ct)
    {
        if (!string.Equals(User.Identity?.Name, hostname, StringComparison.Ordinal))
        {
            return Forbid();
        }

        var found = await updateService.AcknowledgeInstallAsync(hostname, request.Outcome, ct);
        return found ? NoContent() : NotFound();
    }
}
