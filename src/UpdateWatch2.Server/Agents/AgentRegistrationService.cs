using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.AgentUpdates;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Agents;

/// <summary>
/// Implements the registration/onboarding state machine, called repeatedly
/// by an agent (see <c>Protocol/AgentApiRoutes.Register</c> agent-side):
///
/// - No token + unknown hostname -> create the Agent row (Approved=false),
///   return a fresh registration token (only its hash is persisted).
/// - No token + hostname already has a row -> Rejected. A fresh,
///   unauthenticated call must never reset or hijack an in-flight
///   registration for a hostname someone else already claimed.
/// - Token present but doesn't match the stored hash -> Rejected.
/// - Token matches, not yet approved -> Pending (idempotent poll).
/// - Token matches, approved, no certificate issued yet -> issue one now
///   (the only time key material crosses the wire — protected by the
///   pinned-CA TLS channel this runs behind, plus this token check),
///   persist its thumbprint/issued/expiry, clear the now-unneeded token
///   hash, return it.
/// - Certificate already issued (regardless of the token presented, or even
///   with none) -> Approved with no certificate. Once delivered, the token
///   is cleared (it's done its job) and no longer gates this steady state,
///   since disclosing "yes, approved" a second time leaks nothing — the
///   certificate itself is never handed out again over THIS endpoint.
///   A lost/wiped agent gets back in via admin-mediated re-issuance
///   (<see cref="IAgentService.ReissueCertificateAsync"/>, updatewatch2-server#8),
///   which clears the thumbprint and mints a fresh registration token — at
///   that point the state machine above runs again exactly as on first
///   contact. An agent that still has a valid certificate gets a fresh one
///   proactively before expiry via the separate <see cref="RenewCertificateAsync"/>
///   (updatewatch2-server#7), authenticated by the current certificate
///   itself rather than a token — deliberately not part of this method.
/// </summary>
public class AgentRegistrationService(
    AppDbContext db,
    ICertificateAuthority ca,
    IAuditLogService auditLog,
    IAdminSettingsStore settingsStore,
    IAgentUpdateService agentUpdateService) : IAgentRegistrationService
{
    public async Task<AgentRegistrationOutcome> RegisterAsync(string hostname, AgentRegisterRequest request, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);

        // Once a certificate has been delivered, the registration token has
        // done its job and was cleared (see below) — from here on, a poll
        // for this hostname never discloses anything sensitive (the
        // certificate is never handed out a second time regardless), so no
        // token check gates reaching this steady state.
        if (agent is not null && agent.ClientCertificateThumbprint is not null)
        {
            return AgentRegistrationOutcome.Approved(certificatePfxBase64: null);
        }

        if (string.IsNullOrEmpty(request.RegistrationToken))
        {
            if (agent is not null)
            {
                return AgentRegistrationOutcome.Rejected("An agent with this hostname is already registered.");
            }

            var (rawToken, hash) = RegistrationTokenHasher.GenerateToken();
            db.Agents.Add(new Agent
            {
                Hostname = hostname,
                DnsName = request.DnsName,
                OperatingSystem = request.OperatingSystem,
                IpAddress = request.IpAddress,
                AgentVersion = request.AgentVersion,
                Approved = false,
                RegistrationTokenHash = hash,
            });
            await db.SaveChangesAsync(ct);
            await auditLog.LogAsync("agent", "agent.register", hostname, ct);

            return AgentRegistrationOutcome.Pending(rawToken);
        }

        if (agent is null || agent.RegistrationTokenHash is null || !RegistrationTokenHasher.Verify(request.RegistrationToken, agent.RegistrationTokenHash))
        {
            return AgentRegistrationOutcome.Rejected("Unknown hostname or registration token mismatch.");
        }

        // Self-reported metadata can change between polls (IP, agent
        // version) while an admin hasn't approved yet — keep it current.
        agent.DnsName = request.DnsName ?? agent.DnsName;
        agent.OperatingSystem = request.OperatingSystem ?? agent.OperatingSystem;
        agent.IpAddress = request.IpAddress ?? agent.IpAddress;
        agent.AgentVersion = request.AgentVersion ?? agent.AgentVersion;

        if (!agent.Approved)
        {
            await db.SaveChangesAsync(ct);
            return AgentRegistrationOutcome.Pending(rawToken: null);
        }

        // agent.ClientCertificateThumbprint is guaranteed null here — the
        // early-return at the top of this method already handles the
        // already-delivered case.
        var issued = ca.IssueAgentLeaf(hostname, TimeSpan.FromDays(settingsStore.Certificate.AgentCertificateValidityDays));
        agent.ClientCertificateThumbprint = issued.ThumbprintSha256;
        agent.ClientCertificateThumbprintSha1 = issued.ThumbprintSha1;
        agent.ClientCertificateIssuedAt = issued.IssuedAt;
        agent.ClientCertificateExpiresAt = issued.ExpiresAt;
        agent.RegistrationTokenHash = null;
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync("agent", "agent.certificate.issued", hostname, ct);

        return AgentRegistrationOutcome.Approved(Convert.ToBase64String(issued.PfxBytes));
    }

    public async Task<AliveRecordResult?> RecordAliveAsync(string hostname, AgentAliveRequest? request, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return null;
        }

        agent.LastAliveAt = DateTimeOffset.UtcNow;

        // Mirrors the same refresh RegisterAsync does on a pre-approval
        // poll (see above) — this is the post-approval equivalent, since
        // RegisterAsync's early-return for an already-certified agent means
        // registration itself never runs again to catch a later change
        // (updatewatch2-agent#6). Null request = an agent build that
        // predates this field — nothing to refresh, not an error.
        if (request is not null)
        {
            agent.DnsName = request.DnsName ?? agent.DnsName;
            agent.OperatingSystem = request.OperatingSystem ?? agent.OperatingSystem;
            agent.IpAddress = request.IpAddress ?? agent.IpAddress;
            agent.AgentVersion = request.AgentVersion ?? agent.AgentVersion;
        }

        await db.SaveChangesAsync(ct);
        var updateOffer = await agentUpdateService.GetOfferForAsync(agent.AgentVersion, ct);
        return new AliveRecordResult(agent.PendingInstallRequestedAt is not null, updateOffer);
    }

    public async Task<RenewCertificateResult> RenewCertificateAsync(string hostname, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);

        // Defense in depth only: reaching this method at all already
        // required presenting a currently-valid client certificate that
        // CertificateValidator resolved to this exact hostname, which in
        // turn requires Approved && a non-null thumbprint. This branch
        // should be unreachable in practice.
        if (agent is null || !agent.Approved || agent.ClientCertificateThumbprint is null)
        {
            return RenewCertificateResult.Failed("Agent is not in a renewable state.");
        }

        var issued = ca.IssueAgentLeaf(hostname, TimeSpan.FromDays(settingsStore.Certificate.AgentCertificateValidityDays));
        agent.ClientCertificateThumbprint = issued.ThumbprintSha256;
        agent.ClientCertificateThumbprintSha1 = issued.ThumbprintSha1;
        agent.ClientCertificateIssuedAt = issued.IssuedAt;
        agent.ClientCertificateExpiresAt = issued.ExpiresAt;
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync("agent", "agent.certificate.renew", hostname, ct);

        return RenewCertificateResult.Succeeded(Convert.ToBase64String(issued.PfxBytes));
    }
}
