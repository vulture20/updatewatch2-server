namespace UpdateWatch2.Server.Audit;

public interface IAuditLogService
{
    /// <summary>Records one administrative or security-relevant action. See CLAUDE.md audit-log requirement.</summary>
    Task LogAsync(string actor, string action, string? details = null, CancellationToken ct = default);

    /// <summary>
    /// A page of the audit log, newest first, optionally filtered to
    /// entries whose actor/action/details contain <paramref name="search"/>
    /// (case-insensitive). Backs the admin UI's Audit Log tab.
    /// <paramref name="page"/> is 1-based. <paramref name="pageSize"/> is
    /// clamped to [1, 200] server-side regardless of what's requested; a
    /// <c>null</c> <paramref name="pageSize"/> is the one, unambiguous "no
    /// limit" representation — every matching entry is returned in one
    /// response (still capped at <c>MaxUnlimitedRows</c>), an explicit admin
    /// opt-in (see <see cref="Admin.IAdminSettingsStore.ItemsPerPage"/>'s own
    /// "unlimited" option) rather than the default; the returned
    /// <c>Page</c>/<c>PageSize</c> reflect the actual single page (1) and
    /// count in that case. This method itself has no separate "unspecified"
    /// concept — normalizing an unspecified/invalid request into a concrete
    /// page size (or genuinely wanting no limit at all) is entirely
    /// <c>AuditLogController</c>'s job, so every value that reaches here
    /// means exactly one thing.
    /// </summary>
    Task<AuditLogPageDto> GetPageAsync(int page, int? pageSize, string? search = null, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes every entry older than <paramref name="retentionDays"/>
    /// and, if any were deleted, records that as an entry of its own
    /// (actor <c>"system"</c>, action <c>"audit-log.purged"</c>). A
    /// <paramref name="retentionDays"/> of 0 or less is the "unlimited"
    /// sentinel (see <see cref="Admin.IAdminSettingsStore.AuditLogRetentionDays"/>)
    /// and is a no-op. Returns the number of entries deleted. Driven
    /// periodically by <see cref="AuditLogRetentionWorker"/>, not called
    /// from any admin-facing endpoint.
    /// </summary>
    Task<int> PurgeOlderThanAsync(int retentionDays, CancellationToken ct = default);
}
