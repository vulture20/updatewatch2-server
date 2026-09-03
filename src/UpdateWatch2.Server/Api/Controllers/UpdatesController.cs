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
    public async Task<IActionResult> GetUpdates(string hostname, CancellationToken ct)
    {
        var updates = await updateService.GetForAgentAsync(hostname, ct);
        return updates is null ? NotFound() : Ok(updates);
    }

    [HttpPost("updates")]
    public async Task<IActionResult> ReportUpdates(string hostname, [FromBody] ReportUpdatesRequest request, CancellationToken ct)
    {
        var found = await updateService.ReportUpdatesAsync(hostname, request, ct);
        return found ? NoContent() : NotFound();
    }

    [HttpPost("install")]
    public async Task<IActionResult> TriggerInstall(string hostname, CancellationToken ct)
    {
        // TODO: replace with the authenticated admin's username once auth is wired up.
        var found = await updateService.TriggerInstallAsync(hostname, triggeredBy: "admin", ct);
        return found ? Accepted() : NotFound();
    }
}
