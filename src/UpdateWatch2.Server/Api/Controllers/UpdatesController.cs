using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    // require the admin cookie session. It's still unauthenticated — real
    // agent authentication (mutual TLS) isn't implemented yet, see
    // updatewatch2-server#3.
    [HttpPost("updates")]
    [AllowAnonymous]
    public async Task<IActionResult> ReportUpdates(string hostname, [FromBody] ReportUpdatesRequest request, CancellationToken ct)
    {
        var found = await updateService.ReportUpdatesAsync(hostname, request, ct);
        return found ? NoContent() : NotFound();
    }

    [HttpPost("install")]
    [Authorize]
    public async Task<IActionResult> TriggerInstall(string hostname, CancellationToken ct)
    {
        var found = await updateService.TriggerInstallAsync(hostname, triggeredBy: User.Identity!.Name!, ct);
        return found ? Accepted() : NotFound();
    }
}
