using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Api;
using UpdateWatch2.Server.Db;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Exposes the four independent version numbers described in CLAUDE.md.
/// Allowed on the agent port too — <c>HeartbeatWorker</c> polls this on
/// every tick for protocol-mismatch detection, over the same connection
/// it uses for everything else (updatewatch2-server#3).
/// </summary>
[ApiController]
[Route("api/version")]
[AllowedOnAgentPort]
public class VersionController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        server = AppVersion.Current,
        protocol = Protocol.ProtocolVersion.Current,
        database = SchemaVersion.Current,
    });
}
