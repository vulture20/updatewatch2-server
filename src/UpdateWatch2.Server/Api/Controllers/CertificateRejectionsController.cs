using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Certificates;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Read-only status for the admin UI's rejected-client-certificate warning
/// banner — recording itself happens in <c>Program.cs</c>'s certificate-
/// authentication events via <see cref="ICertificateRejectionService.RecordAsync"/>,
/// not here.
/// </summary>
[ApiController]
[Route("api/admin/certificate-rejections")]
[Authorize]
public class CertificateRejectionsController(ICertificateRejectionService rejectionService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await rejectionService.GetStatusAsync(ct));
}
