namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// One record of an administrative or security-relevant action, per the
/// audit-log requirement in CLAUDE.md.
/// </summary>
public class AuditLogEntry
{
    public int Id { get; set; }

    /// <summary>Who performed the action (admin username, or agent hostname for agent-initiated events).</summary>
    public required string Actor { get; set; }

    /// <summary>Short machine-readable action name, e.g. "agent.approve", "login.failed".</summary>
    public required string Action { get; set; }

    public string? Details { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
