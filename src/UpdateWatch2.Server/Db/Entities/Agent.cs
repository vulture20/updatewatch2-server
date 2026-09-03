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

    /// <summary>Thumbprint of the client certificate issued to this agent after approval.</summary>
    public string? ClientCertificateThumbprint { get; set; }

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
