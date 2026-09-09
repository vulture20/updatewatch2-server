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
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? search, CancellationToken ct) =>
        Ok(await auditLog.GetPageAsync(page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize, search, ct));
}
