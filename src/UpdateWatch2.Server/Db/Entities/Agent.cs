namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// A managed endpoint (Windows today, Linux planned). Identified uniquely by
/// <see cref="Hostname"/> — see CLAUDE.md "Agents are identified by hostname".
/// </summary>
public class Agent
{
    public int Id { get; set; }

    public required string Hostname { get; set; }

    public string? DnsName { get; set; }

    public string? OperatingSystem { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Version string reported by the agent itself (independent of protocol/server versions).</summary>
    public string? AgentVersion { get; set; }

    /// <summary>
    /// SHA-256 thumbprint of the client certificate issued to this agent after
    /// approval. Also doubles as the one-shot-delivery marker for
    /// <see cref="RegistrationTokenHash"/>'s flow: once set, the certificate has
    /// already been handed to the agent and will never be re-issued/re-sent —
    /// see AgentRegistrationService.
    /// </summary>
    public string? ClientCertificateThumbprint { get; set; }

    /// <summary>When the client certificate identified by <see cref="ClientCertificateThumbprint"/> was issued.</summary>
    public DateTimeOffset? ClientCertificateIssuedAt { get; set; }

    /// <summary>When the client certificate identified by <see cref="ClientCertificateThumbprint"/> expires.</summary>
    public DateTimeOffset? ClientCertificateExpiresAt { get; set; }

    /// <summary>
    /// SHA-256 hash of the opaque registration token handed to this agent on
    /// first contact (never the raw token — same secret-hygiene convention as
    /// password/AD-bind-password storage elsewhere in this codebase). Used to
    /// prevent a different host from hijacking an in-flight, not-yet-approved
    /// registration for the same hostname. Not needed once a certificate has
    /// been delivered, so it's cleared at that point.
    /// </summary>
    public string? RegistrationTokenHash { get; set; }

    /// <summary>
    /// False until an admin manually confirms this agent (individually or via bulk approval).
    /// No client certificate is issued, and no authenticated traffic is accepted, before approval.
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>Set from the agent's most recent update-check report; independent of update installation.</summary>
    public bool RebootRequired { get; set; }

    public int PendingUpdateCount { get; set; }

    public DateTimeOffset? LastAliveAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
