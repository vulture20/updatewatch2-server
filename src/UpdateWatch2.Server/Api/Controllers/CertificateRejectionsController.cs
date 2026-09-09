using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Certificates;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Status plus acknowledgement for the admin UI's rejected-client-
/// certificate warning banner — recording a rejection itself happens in
/// <c>Program.cs</c>'s certificate-authentication events via
/// <see cref="ICertificateRejectionService.RecordAsync"/>, not here.
/// </summary>
[ApiController]
[Route("api/admin/certificate-rejections")]
[Authorize]
public class CertificateRejectionsController(ICertificateRejectionService rejectionService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await rejectionService.GetStatusAsync(ct));

    /// <summary>
    /// Silences the banner for every rejection recorded so far — a shared,
    /// audit-logged action visible to every admin session, not a per-
    /// session dismiss (see <see cref="ICertificateRejectionService.AcknowledgeAsync"/>).
    /// Returns the refreshed status (now 0, unless a new rejection landed
    /// in the moment between the admin clicking and this running).
    /// </summary>
    [HttpPost("acknowledge")]
    public async Task<IActionResult> Acknowledge(CancellationToken ct)
    {
        await rejectionService.AcknowledgeAsync(User.Identity!.Name!, ct);
        return Ok(await rejectionService.GetStatusAsync(ct));
    }
}
