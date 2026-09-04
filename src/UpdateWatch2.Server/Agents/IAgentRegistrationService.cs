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
}
