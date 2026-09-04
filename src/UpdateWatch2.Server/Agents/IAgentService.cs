namespace UpdateWatch2.Server.Agents;

public interface IAgentService
{
    Task<IReadOnlyList<AgentListItemDto>> GetAllAsync(CancellationToken ct = default);

    Task<AgentDetailDto?> GetByHostnameAsync(string hostname, CancellationToken ct = default);

    /// <summary>
    /// Approves a single unconfirmed agent. Returns false if no agent with
    /// that hostname exists. Certificate issuance (see CLAUDE.md onboarding
    /// flow) does not happen synchronously here — it happens lazily, the
    /// next time the now-approved agent polls
    /// <see cref="AgentRegistrationService.RegisterAsync"/>.
    /// </summary>
    Task<bool> ApproveAsync(string hostname, string approvedBy, CancellationToken ct = default);

    /// <summary>Approves several agents at once (bulk approval from the overview list).</summary>
    Task<BulkApproveResult> ApproveManyAsync(IReadOnlyList<string> hostnames, string approvedBy, CancellationToken ct = default);

    /// <summary>
    /// Admin-initiated recovery for an agent that lost its certificate
    /// (wiped/reinstalled, corrupted local store — updatewatch2-server#8).
    /// Clears the agent's certificate fields and mints a fresh, single-use
    /// registration token — <see cref="Db.Entities.Agent.Approved"/> is left
    /// true, no re-approval needed, since the agent was already vetted once. The raw
    /// token is returned exactly once for the admin to place into the
    /// agent's local configuration; from there
    /// <see cref="AgentRegistrationService.RegisterAsync"/>'s existing
    /// state machine runs again exactly as on first contact. Fails if the
    /// agent is unknown or not currently approved (an unapproved agent
    /// never had a certificate to lose).
    /// </summary>
    Task<ReissueCertificateResult> ReissueCertificateAsync(string hostname, string initiatedBy, CancellationToken ct = default);
}
