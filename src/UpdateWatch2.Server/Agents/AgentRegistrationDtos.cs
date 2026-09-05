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
/// Optional body of <c>POST /api/agents/{hostname}/alive</c>
/// (updatewatch2-agent#6) — an agent's self-reported metadata can change
/// after onboarding (DHCP lease renewal, an OS upgrade, a hostname/domain
/// change, an agent binary upgrade), but <see cref="AgentRegistrationService.RegisterAsync"/>
/// never runs again for an already-certified agent, so the alive heartbeat
/// is the only remaining channel to keep it current. Nullable/all-optional
/// rather than required: an agent built before this field existed sends no
/// body at all, and that must keep working exactly as before (just with no
/// metadata refresh), not fail the heartbeat.
/// </summary>
public record AgentAliveRequest(string? DnsName, string? OperatingSystem, string? IpAddress, string? AgentVersion);

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
