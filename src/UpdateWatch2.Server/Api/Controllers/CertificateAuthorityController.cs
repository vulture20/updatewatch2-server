using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Admin-facing CA root rotation (updatewatch2-server#6) — cookie-session
/// gated, distinct from the agent-facing, mTLS-gated CA-certificate routes
/// on <see cref="AgentProtocolController"/>. Three explicit, one-way steps
/// rather than a single "rotate now" action, because activating
/// immediately re-issues the server's own TLS leaf under the new root —
/// see <see cref="ICertificateAuthority.ActivateRotation"/>'s remarks on why
/// an admin should let the pending root propagate to already-onboarded
/// agents first via their own heartbeat cadence.
/// </summary>
[ApiController]
[Route("api/admin/certificate-authority")]
[Authorize]
public class CertificateAuthorityController(ICertificateAuthority ca, IAuditLogService auditLog) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(ca.GetRotationStatus());

    [HttpPost("prepare")]
    public async Task<IActionResult> Prepare(CancellationToken ct)
    {
        var pending = ca.PrepareRotation();
        await auditLog.LogAsync(User.Identity!.Name!, "ca.rotation.prepared", pending.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), ct);
        return Ok(ca.GetRotationStatus());
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate(CancellationToken ct)
    {
        if (ca.PendingRootCertificate is null)
        {
            return Conflict(new { message = "No pending root to activate — prepare a rotation first." });
        }

        ca.ActivateRotation();
        await auditLog.LogAsync(User.Identity!.Name!, "ca.rotation.activated", ca.RootCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), ct);
        return Ok(ca.GetRotationStatus());
    }

    [HttpPost("retire-previous")]
    public async Task<IActionResult> RetirePrevious(CancellationToken ct)
    {
        if (ca.PreviousRootCertificate is null)
        {
            return Conflict(new { message = "No previous root to retire." });
        }

        var thumbprint = ca.PreviousRootCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
        ca.RetirePreviousRoot();
        await auditLog.LogAsync(User.Identity!.Name!, "ca.rotation.retired", thumbprint, ct);
        return Ok(ca.GetRotationStatus());
    }
}
