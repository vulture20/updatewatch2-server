namespace UpdateWatch2.Server.Audit;

public interface IAuditLogService
{
    /// <summary>Records one administrative or security-relevant action. See CLAUDE.md audit-log requirement.</summary>
    Task LogAsync(string actor, string action, string? details = null, CancellationToken ct = default);
}
