using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Db;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>Exposes the four independent version numbers described in CLAUDE.md.</summary>
[ApiController]
[Route("api/version")]
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
