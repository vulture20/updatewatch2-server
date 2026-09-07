using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Agents;
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
public class CertificateAuthorityController(ICertificateAuthority ca, IAgentService agentService, IAuditLogService auditLog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await BuildStatusAsync(ct));

    [HttpPost("prepare")]
    public async Task<IActionResult> Prepare(CancellationToken ct)
    {
        var pending = ca.PrepareRotation();
        await auditLog.LogAsync(User.Identity!.Name!, "ca.rotation.prepared", pending.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), ct);
        return Ok(await BuildStatusAsync(ct));
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
        return Ok(await BuildStatusAsync(ct));
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
        return Ok(await BuildStatusAsync(ct));
    }

    /// <summary>
    /// Flattens <see cref="ICertificateAuthority.GetRotationStatus"/> together
    /// with <see cref="IAgentService.GetCaRotationImpactAsync"/> into one
    /// response — the same "compose several pieces into one flat object"
    /// shape <see cref="AgentProtocolController.Alive"/> already uses.
    /// <see cref="ICertificateAuthority"/> deliberately stays DB-unaware
    /// (see its own class-level remarks), so the agent-impact half of this
    /// is queried through <see cref="IAgentService"/> instead, not folded
    /// into <see cref="Certificates.CaRotationStatus"/> itself.
    /// </summary>
    private async Task<object> BuildStatusAsync(CancellationToken ct)
    {
        var status = ca.GetRotationStatus();
        var impact = await agentService.GetCaRotationImpactAsync(status.PreviousThumbprint, ct);
        return new
        {
            status.CurrentThumbprint,
            status.CurrentNotAfter,
            status.PreviousThumbprint,
            status.PreviousNotAfter,
            status.PendingThumbprint,
            status.PendingNotAfter,
            stillOnPreviousRootCount = impact.StillOnPreviousRootCount,
            stillOnPreviousRootHostnames = impact.StillOnPreviousRootHostnames,
            unknownRootAgentCount = impact.UnknownRootAgentCount,
        };
    }
}
