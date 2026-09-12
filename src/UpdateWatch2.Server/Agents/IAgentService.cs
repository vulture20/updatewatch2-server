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

    /// <summary>
    /// Permanently removes an agent (and, via the FK cascade configured in
    /// <see cref="Db.AppDbContext"/>, its <see cref="Db.Entities.UpdateItem"/>
    /// rows) — for a decommissioned machine or a mistaken/test registration
    /// an admin never wants to see again, as opposed to
    /// <see cref="ReissueCertificateAsync"/>, which keeps the agent but
    /// gives it a fresh identity to reconnect with. Effective immediately,
    /// not just cosmetically: <c>CertificateValidator.ValidateAsync</c>
    /// resolves a client certificate to an agent by a DB lookup, so once
    /// the row is gone, the agent's still-cryptographically-valid
    /// certificate stops authenticating on its very next request — no
    /// separate revocation-list mechanism needed. If the same hostname
    /// registers again later, it starts over as a brand-new, unapproved
    /// agent, exactly like first contact. Returns false if no agent with
    /// that hostname exists.
    /// </summary>
    Task<bool> DeleteAsync(string hostname, string initiatedBy, CancellationToken ct = default);

    /// <summary>
    /// Remote-triggers a reboot of the agent's own machine — not just the
    /// agent's own service process, and not an update install (see
    /// <see cref="Db.Entities.Agent.PendingRebootRequestedAt"/>'s doc
    /// comment, and CLAUDE.md's "update installation never triggers a
    /// reboot itself... the admin decides when to actually trigger a
    /// reboot" rule, which this implements). Sets that field, which the
    /// agent picks up on its next alive heartbeat, mirroring
    /// <c>Updates.IUpdateService.TriggerInstallAsync</c>'s exact delivery
    /// mechanism. Fire-and-forget from the admin's perspective, like that
    /// sibling call. Returns false if no agent with that hostname exists.
    /// </summary>
    Task<bool> TriggerRebootAsync(string hostname, string triggeredBy, CancellationToken ct = default);

    /// <summary>
    /// The agent's acknowledgement that it acted on a pending reboot
    /// request — clears <see cref="Db.Entities.Agent.PendingRebootRequestedAt"/>
    /// regardless of <paramref name="outcome"/> and records the
    /// outcome/timestamp/<paramref name="errorDetail"/> for the admin UI,
    /// mirroring <c>Updates.IUpdateService.AcknowledgeInstallAsync</c>
    /// exactly. <paramref name="outcome"/> only ever reflects whether the
    /// platform's reboot command was scheduled successfully — whether the
    /// machine has actually come back up is instead visible via
    /// <see cref="Db.Entities.Agent.BootTimeUtc"/> jumping forward on a
    /// later heartbeat. Returns false if no agent with that hostname
    /// exists.
    /// </summary>
    Task<bool> AcknowledgeRebootAsync(string hostname, RebootOutcome outcome, string? errorDetail, CancellationToken ct = default);

    /// <summary>
    /// How many/which approved agents' <see cref="Db.Entities.Agent.IssuingRootThumbprint"/>
    /// still matches <paramref name="previousRootThumbprintSha256"/> — i.e.
    /// would stop authenticating if that root were retired right now.
    /// Returns <see cref="CaRotationImpactDto.None"/> without querying when
    /// <paramref name="previousRootThumbprintSha256"/> is null (no active
    /// rotation overlap window, per <see cref="Certificates.ICertificateAuthority.PreviousRootCertificate"/>).
    /// </summary>
    Task<CaRotationImpactDto> GetCaRotationImpactAsync(string? previousRootThumbprintSha256, CancellationToken ct = default);
}
