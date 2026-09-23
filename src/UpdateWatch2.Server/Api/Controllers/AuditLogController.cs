using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Audit;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>Read-only, paginated view of the audit log — backs Administration's Audit Log tab.</summary>
[ApiController]
[Route("api/admin/audit-log")]
[Authorize]
public class AuditLogController(IAuditLogService auditLog) : ControllerBase
{
    // "Unlimited" is its own explicit query parameter, not encoded into
    // pageSize as a magic negative value — the AdminSettings.ItemsPerPage
    // "unlimited" sentinel (0 there) never reaches this layer either; the
    // admin UI translates it into unlimited=true before calling this
    // endpoint. pageSize itself now only ever means "the requested page
    // size" — 0 (or omitted, e.g. a manual Swagger call) still falls back
    // to the 50 default, but there is no longer a second, negative-number
    // meaning layered on top of it. GetPageAsync's own pageSize parameter
    // is `null` exactly when unlimited is true — the one, unambiguous "no
    // limit" representation that reaches the service (see its own doc
    // comment).
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] bool unlimited,
        [FromQuery] string? search,
        CancellationToken ct) =>
        Ok(await auditLog.GetPageAsync(page == 0 ? 1 : page, unlimited ? null : (pageSize == 0 ? 50 : pageSize), search, ct));
}
