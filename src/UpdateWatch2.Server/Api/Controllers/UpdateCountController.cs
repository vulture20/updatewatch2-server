using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Agents;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Deliberately unauthenticated (no <c>[Authorize]</c>, matching
/// <see cref="HealthController"/>'s model) — the single fleet-wide pending
/// update count, for an external display with no session of its own (e.g.
/// a Stream Deck button), at the user's explicit request. Not marked
/// <see cref="AllowedOnAgentPortAttribute"/>, so it's only reachable on the
/// browser-facing port (8795), never the agent-facing mTLS port (8796) —
/// see that attribute's own doc comment and <c>Program.cs</c>'s port-gating
/// middleware. Deliberately exposes only the one aggregate number, never a
/// per-agent breakdown, to keep an unauthenticated caller from learning
/// anything about individual agents.
/// </summary>
[ApiController]
[Route("api/update-count")]
public class UpdateCountController(IAgentService agentService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(new { pendingUpdateCount = await agentService.GetTotalPendingUpdateCountAsync(ct) });
}
