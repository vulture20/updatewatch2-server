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
