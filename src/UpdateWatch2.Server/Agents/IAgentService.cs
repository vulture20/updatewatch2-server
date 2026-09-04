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
}
