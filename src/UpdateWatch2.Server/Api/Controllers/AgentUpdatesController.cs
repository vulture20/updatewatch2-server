using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.AgentUpdates;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Read-only status for the admin UI's agent-auto-update section
/// (updatewatch2-server#14) — the enabled/token toggle itself lives on
/// the existing <c>PUT /api/admin/settings</c> (see
/// <see cref="AdminController"/>), same as every other admin setting;
/// this is just the additional, non-editable "what's the newest version
/// this server currently knows about" state that setting doesn't carry.
/// </summary>
[ApiController]
[Route("api/admin/agent-update-status")]
[Authorize]
public class AgentUpdatesController(IAgentUpdateService agentUpdateService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await agentUpdateService.GetStatusAsync(ct));
}
