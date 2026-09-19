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
    // pageSize == 0 here means "the caller didn't specify one" (e.g. a
    // manual Swagger call) and defaults to 50 — unrelated to the
    // AdminSettings.ItemsPerPage "unlimited" sentinel, which is also 0 but
    // never reaches this query parameter: the frontend always translates
    // that setting into a distinct pageSize=-1 before calling this
    // endpoint, which passes straight through to GetPageAsync unchanged
    // (see that method's own doc comment for the -1 "no limit" branch).
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? search, CancellationToken ct) =>
        Ok(await auditLog.GetPageAsync(page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize, search, ct));
}
