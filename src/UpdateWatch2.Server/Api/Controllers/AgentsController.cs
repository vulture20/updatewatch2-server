using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Agents;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Agent overview, detail view, and onboarding approval. Authentication
/// (admin session for these endpoints, mutual certificate for agent-facing
/// endpoints) is not wired up yet — see CLAUDE.md onboarding flow.
/// </summary>
[ApiController]
[Route("api/agents")]
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
        // TODO: replace with the authenticated admin's username once auth is wired up.
        var approved = await agentService.ApproveAsync(hostname, approvedBy: "admin", ct);
        return approved ? NoContent() : NotFound();
    }

    [HttpPost("approve")]
    public async Task<IActionResult> ApproveMany([FromBody] BulkApproveRequest request, CancellationToken ct)
    {
        var result = await agentService.ApproveManyAsync(request.Hostnames, approvedBy: "admin", ct);
        return Ok(result);
    }
}
