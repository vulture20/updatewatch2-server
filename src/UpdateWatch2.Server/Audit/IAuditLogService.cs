namespace UpdateWatch2.Server.Audit;

public interface IAuditLogService
{
    /// <summary>Records one administrative or security-relevant action. See CLAUDE.md audit-log requirement.</summary>
    Task LogAsync(string actor, string action, string? details = null, CancellationToken ct = default);

    /// <summary>
    /// A page of the audit log, newest first, optionally filtered to
    /// entries whose actor/action/details contain <paramref name="search"/>
    /// (case-insensitive). Backs the admin UI's Audit Log tab.
    /// <paramref name="page"/> is 1-based; <paramref name="pageSize"/> is
    /// clamped to [1, 200] server-side regardless of what's requested, so
    /// the table can never be asked to render an unbounded page.
    /// </summary>
    Task<AuditLogPageDto> GetPageAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
}
