using System.Security.Cryptography.X509Certificates;
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

    /// <summary>
    /// Lets an admin obtain the current CA root's raw bytes over their own
    /// already-authenticated session — closes the trust-on-first-use (TOFU)
    /// window a freshly installed agent otherwise relies on
    /// (<c>RegistrationWorker.EnsureCaPinnedAsync</c> on the agent side, a
    /// separate repo): download this file ahead of time and hand
    /// it to the installer (NSIS <c>/CACERT=</c>, or manually placed at
    /// <c>/etc/updatewatch2/ca.pem</c> on Linux before the service's first
    /// start) so the agent never has to trust whatever CA a first, possibly
    /// intercepted connection hands it. Deliberately GET, not POST — purely
    /// read-only, and a plain <c>&lt;a href&gt;</c> only navigates for GET.
    /// Same DER export (<see cref="X509ContentType.Cert"/>) as the
    /// anonymous, agent-facing <see cref="AgentProtocolController.CaCertificate"/>
    /// — which stays untouched and is still what a genuinely un-pre-seeded
    /// agent falls back to; this is strictly an additional, authenticated
    /// path to the same bytes, not a replacement.
    /// </summary>
    [HttpGet("download")]
    public async Task<IActionResult> Download(CancellationToken ct)
    {
        var bytes = ca.RootCertificate.Export(X509ContentType.Cert);
        await auditLog.LogAsync(
            User.Identity!.Name!,
            "ca.root.downloaded",
            ca.RootCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256),
            ct);
        return File(bytes, "application/x-x509-ca-cert", "updatewatch2-ca.crt");
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

        // Subject/Issuer/SerialNumber/NotBefore are additive fields
        // (server v0.30.4, at the user's request to show "all available
        // info" for every relevant certificate on the new Info tab) — read
        // straight from the live X509Certificate2 instances rather than
        // added to ICertificateAuthority.GetRotationStatus/CaRotationStatus
        // itself, so the CA's own core rotation-status shape (and every
        // test/caller already built against it) stays untouched. The
        // server's own agent-facing TLS leaf (ca.CurrentServerLeaf) had no
        // admin-facing representation anywhere before this — not part of
        // "rotation status" at all, but exactly the other "relevant
        // certificate" the user asked to see alongside the CA roots.
        var serverLeaf = ca.CurrentServerLeaf;
        return new
        {
            status.CurrentThumbprint,
            status.CurrentNotAfter,
            CurrentNotBefore = ca.RootCertificate.NotBefore,
            CurrentSubject = ca.RootCertificate.Subject,
            CurrentIssuer = ca.RootCertificate.Issuer,
            CurrentSerialNumber = ca.RootCertificate.SerialNumber,
            status.PreviousThumbprint,
            status.PreviousNotAfter,
            PreviousNotBefore = ca.PreviousRootCertificate?.NotBefore,
            PreviousSubject = ca.PreviousRootCertificate?.Subject,
            PreviousIssuer = ca.PreviousRootCertificate?.Issuer,
            PreviousSerialNumber = ca.PreviousRootCertificate?.SerialNumber,
            status.PendingThumbprint,
            status.PendingNotAfter,
            PendingNotBefore = ca.PendingRootCertificate?.NotBefore,
            PendingSubject = ca.PendingRootCertificate?.Subject,
            PendingIssuer = ca.PendingRootCertificate?.Issuer,
            PendingSerialNumber = ca.PendingRootCertificate?.SerialNumber,
            stillOnPreviousRootCount = impact.StillOnPreviousRootCount,
            stillOnPreviousRootHostnames = impact.StillOnPreviousRootHostnames,
            unknownRootAgentCount = impact.UnknownRootAgentCount,
            ServerLeafThumbprint = serverLeaf.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256),
            ServerLeafSubject = serverLeaf.Subject,
            ServerLeafIssuer = serverLeaf.Issuer,
            ServerLeafSerialNumber = serverLeaf.SerialNumber,
            ServerLeafNotBefore = serverLeaf.NotBefore,
            ServerLeafNotAfter = serverLeaf.NotAfter,
        };
    }
}
