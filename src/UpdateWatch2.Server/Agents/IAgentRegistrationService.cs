namespace UpdateWatch2.Server.Agents;

/// <summary>
/// The agent-facing half of onboarding — self-registration, the
/// poll-until-approved loop, and (once approved) one-shot client
/// certificate issuance. Distinct from <see cref="IAgentService"/>, which is
/// the admin-facing half (list/approve). See CLAUDE.md "Certificate-based
/// mutual auth is the security backbone".
/// </summary>
public interface IAgentRegistrationService
{
    Task<AgentRegistrationOutcome> RegisterAsync(string hostname, AgentRegisterRequest request, CancellationToken ct = default);

    /// <summary>Records an alive heartbeat. Returns false if no agent with that hostname exists.</summary>
    Task<bool> RecordAliveAsync(string hostname, CancellationToken ct = default);

    /// <summary>
    /// Issues a fresh client certificate for an agent that already has one,
    /// replacing it (updatewatch2-server#7). Callable only by an agent
    /// presenting its CURRENT still-valid certificate — see
    /// <see cref="Api.Controllers.AgentProtocolController.Renew"/>, gated by
    /// the <c>AgentCertificate</c> policy. Fails if the agent is unknown,
    /// unapproved, or has never been issued a certificate at all (that case
    /// is admin-mediated re-issuance, <see cref="IAgentService.ReissueCertificateAsync"/>,
    /// not this).
    /// </summary>
    Task<RenewCertificateResult> RenewCertificateAsync(string hostname, CancellationToken ct = default);
}
