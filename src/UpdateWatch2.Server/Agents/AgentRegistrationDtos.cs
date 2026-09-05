namespace UpdateWatch2.Server.Agents;

/// <summary>
/// Body of a <c>POST /api/agents/{hostname}/register</c> call — hostname
/// itself comes from the route, not this body (the route is the single
/// source of truth for identity, per CLAUDE.md "Agents are identified by
/// hostname"). <see cref="RegistrationToken"/> is omitted on an agent's very
/// first contact and present on every poll after that — see
/// <see cref="AgentRegistrationService"/>'s state-machine doc comment.
/// </summary>
public record AgentRegisterRequest(
    string? DnsName,
    string? OperatingSystem,
    string? IpAddress,
    string? AgentVersion,
    string? ProtocolVersion,
    string? RegistrationToken);

public enum AgentRegistrationStatus
{
    /// <summary>No/mismatched token for an already-known hostname, or a token that doesn't verify — the anti-hijack case.</summary>
    Rejected,

    /// <summary>Registered (or already known) but not yet approved by an admin.</summary>
    Pending,

    /// <summary>Approved. <see cref="AgentRegistrationOutcome.CertificatePfxBase64"/> carries the cert exactly once.</summary>
    Approved,
}

public record AgentRegistrationOutcome(AgentRegistrationStatus Status, string? RegistrationToken, string? CertificatePfxBase64, string? FailureReason)
{
    public static AgentRegistrationOutcome Rejected(string reason) => new(AgentRegistrationStatus.Rejected, null, null, reason);

    public static AgentRegistrationOutcome Pending(string? rawToken) => new(AgentRegistrationStatus.Pending, rawToken, null, null);

    public static AgentRegistrationOutcome Approved(string? certificatePfxBase64) => new(AgentRegistrationStatus.Approved, null, certificatePfxBase64, null);
}

/// <summary>
/// Result of a recorded alive heartbeat (updatewatch2-server#10) —
/// <see cref="InstallRequested"/> mirrors <c>Agent.PendingInstallRequestedAt</c>
/// being set, so <see cref="Api.Controllers.AgentProtocolController.Alive"/>
/// can hand it back to the agent in the same round-trip rather than needing
/// a second poll endpoint.
/// </summary>
public record AliveRecordResult(bool InstallRequested);

/// <summary>
/// Result of <c>POST /api/agents/{hostname}/renew</c> (updatewatch2-server#7)
/// — reached only over the agent-facing mTLS listener, authenticated by the
/// agent's CURRENT still-valid client certificate rather than a token. Not
/// to be confused with <see cref="AgentRegistrationOutcome"/>: renewal never
/// touches <see cref="AgentRegistrationStatus"/> or the registration-token
/// flow, it only re-issues a leaf for an agent already fully onboarded.
/// </summary>
public record RenewCertificateResult(bool Success, string? CertificatePfxBase64, string? FailureReason)
{
    public static RenewCertificateResult Failed(string reason) => new(false, null, reason);

    public static RenewCertificateResult Succeeded(string certificatePfxBase64) => new(true, certificatePfxBase64, null);
}
