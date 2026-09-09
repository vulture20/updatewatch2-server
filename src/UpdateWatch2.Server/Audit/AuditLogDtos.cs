namespace UpdateWatch2.Server.Audit;

public record AuditLogEntryDto(int Id, DateTimeOffset Timestamp, string Actor, string Action, string? Details);

/// <summary>One page of the audit log, newest first — see <see cref="IAuditLogService.GetPageAsync"/>.</summary>
public record AuditLogPageDto(IReadOnlyList<AuditLogEntryDto> Entries, int TotalCount, int Page, int PageSize);
