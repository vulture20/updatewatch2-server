namespace UpdateWatch2.Server.Agents;

/// <summary>Row shape for the main agent overview list.</summary>
public record AgentListItemDto(
    string Hostname,
    bool Approved,
    bool RebootRequired,
    int PendingUpdateCount);

/// <summary>Full shape for the per-agent detail view.</summary>
public record AgentDetailDto(
    string Hostname,
    string? DnsName,
    string? OperatingSystem,
    string? IpAddress,
    string? AgentVersion,
    bool Approved,
    bool RebootRequired,
    int PendingUpdateCount,
    DateTimeOffset? LastAliveAt,
    string? ClientCertificateThumbprint,
    DateTimeOffset? ClientCertificateIssuedAt,
    DateTimeOffset? ClientCertificateExpiresAt);

public record BulkApproveRequest(IReadOnlyList<string> Hostnames);

public record BulkApproveResult(int ApprovedCount, IReadOnlyList<string> NotFoundHostnames);

/// <summary>
/// Result of an admin-initiated certificate re-issuance (updatewatch2-server#8).
/// <see cref="RegistrationToken"/> is the raw, one-shot registration token —
/// returned exactly once here, never persisted or retrievable again — for
/// the admin to place into the affected agent's local configuration.
/// </summary>
public record ReissueCertificateResult(bool Success, string? RegistrationToken, string? FailureReason)
{
    public static ReissueCertificateResult Failed(string reason) => new(false, null, reason);

    public static ReissueCertificateResult Succeeded(string registrationToken) => new(true, registrationToken, null);
}
