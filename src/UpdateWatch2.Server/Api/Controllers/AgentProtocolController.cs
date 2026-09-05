using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Certificates;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// The agent-facing protocol endpoints (register, alive) — mutual-TLS
/// gated, distinct from the admin-facing, cookie-session-gated
/// <see cref="AgentsController"/>. See CLAUDE.md "Certificate-based mutual
/// auth is the security backbone" and updatewatch2-server#1/#3.
/// </summary>
[ApiController]
[Route("api/agents/{hostname}")]
public class AgentProtocolController(IAgentRegistrationService registrationService, ICertificateAuthority ca) : ControllerBase
{
    // Deliberately anonymous: an agent's first contact has no client
    // certificate to authenticate with yet. Bootstrap trust (the token
    // check, plus the pinned-CA TLS channel this runs behind) is handled
    // inside AgentRegistrationService itself — see its doc comment for the
    // full state machine.
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(string hostname, [FromBody] AgentRegisterRequest request, CancellationToken ct)
    {
        var outcome = await registrationService.RegisterAsync(hostname, request, ct);
        return outcome.Status switch
        {
            AgentRegistrationStatus.Rejected => Conflict(new { message = outcome.FailureReason }),
            _ => Ok(new
            {
                approved = outcome.Status == AgentRegistrationStatus.Approved,
                registrationToken = outcome.RegistrationToken,
                certificate = outcome.CertificatePfxBase64,
                protocolVersion = Protocol.ProtocolVersion.Current,
            }),
        };
    }

    [HttpPost("alive")]
    [Authorize(Policy = CertificateAuthenticationSetup.AgentCertificatePolicy)]
    public async Task<IActionResult> Alive(string hostname, [FromBody] AgentAliveRequest? request, CancellationToken ct)
    {
        // Defense in depth: a validly-approved agent for host A must not be
        // able to tamper with the URL and post as host B, even though its
        // certificate was only ever accepted because it matched host A.
        if (!string.Equals(User.Identity?.Name, hostname, StringComparison.Ordinal))
        {
            return Forbid();
        }

        // request is nullable and model binding leaves it null when there's
        // no body at all — an agent build older than updatewatch2-agent#6
        // still posts alive with no body, and that must keep working.
        var result = await registrationService.RecordAliveAsync(hostname, request, ct);
        // Was a bare 204 No Content before installRequested existed to
        // report (updatewatch2-server#10) — protocol version bumped
        // alongside this change (see Protocol/ProtocolVersion.cs) since a
        // pre-#10 agent build only ever checked the status code, never a
        // body, so this is additive rather than actually breaking, but the
        // wire shape did change.
        return result is null ? NotFound() : Ok(new { installRequested = result.InstallRequested });
    }

    // Distinct from Register: this is how an already-certified agent gets a
    // fresh certificate before its current one expires (updatewatch2-server#7)
    // — authenticated by presenting that CURRENT certificate over mTLS, not
    // a registration token. See AgentRegistrationService.RenewCertificateAsync.
    [HttpPost("renew")]
    [Authorize(Policy = CertificateAuthenticationSetup.AgentCertificatePolicy)]
    public async Task<IActionResult> Renew(string hostname, CancellationToken ct)
    {
        if (!string.Equals(User.Identity?.Name, hostname, StringComparison.Ordinal))
        {
            return Forbid();
        }

        var result = await registrationService.RenewCertificateAsync(hostname, ct);
        return result.Success
            ? Ok(new { certificate = result.CertificatePfxBase64 })
            : Conflict(new { message = result.FailureReason });
    }

    // Not per-agent — overrides the controller's route prefix. Anonymous by
    // necessity: this is exactly what an agent bootstraps its trust from
    // before it has anything else to authenticate with. Only ever exposes
    // the CA's public certificate, never its private key.
    [HttpGet("/api/agent/ca-certificate")]
    [AllowAnonymous]
    public IActionResult CaCertificate()
    {
        var bytes = ca.RootCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert);
        return File(bytes, "application/x-x509-ca-cert");
    }

    // Additive alongside CaCertificate above, not a replacement — that
    // endpoint's single-DER-cert contract stays exactly as-is for bootstrap
    // TOFU trust (updatewatch2-agent#1) and any agent build that predates
    // rotation support (updatewatch2-server#6). This one hands out every
    // root the CA currently knows about (current + previous + a pending one
    // not yet active) as a PKCS7 certs-only bundle, so a rotation-aware
    // agent can pre-trust an upcoming root ahead of an admin activating it —
    // see ICertificateAuthority.AllKnownRootCertificates. Anonymous, like
    // the singular endpoint: these are all public certificates.
    [HttpGet("/api/agent/ca-certificates")]
    [AllowAnonymous]
    public IActionResult CaCertificateBundle()
    {
        var bytes = ca.AllKnownRootCertificates.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs7)!;
        return File(bytes, "application/pkcs7-mime");
    }
}
