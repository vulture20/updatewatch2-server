using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Agents;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Agent overview, detail view, and onboarding approval. Requires an admin
/// session (see AuthController). Agent self-registration isn't implemented
/// yet — that will be a separate, mutual-TLS-authenticated endpoint, see
/// updatewatch2-server#3, not a route on this admin-facing controller.
/// </summary>
[ApiController]
[Route("api/agents")]
[Authorize]
public class AgentsController(IAgentService agentService) : ControllerBase
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
}
