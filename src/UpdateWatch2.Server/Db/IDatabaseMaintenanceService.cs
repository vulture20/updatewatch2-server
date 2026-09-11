namespace UpdateWatch2.Server.Db;

/// <summary>
/// SQLite never returns freed disk space to the OS on its own — deleting
/// rows (audit log retention purges, an agent's own update-item churn on
/// every report) just marks the pages they occupied as free within the
/// <c>.sqlite</c> file, which then only ever grows, never shrinks, unless
/// something explicitly reclaims them. This service is that something —
/// see <see cref="DatabaseVacuumWorker"/> for the periodic side, and
/// <c>Program.cs</c> for the one-time startup conversion.
/// </summary>
public interface IDatabaseMaintenanceService
{
    /// <summary>
    /// One-time, idempotent: switches this database to SQLite's
    /// <c>auto_vacuum = INCREMENTAL</c> mode if it isn't already — the
    /// mode that tracks freed pages without eagerly reclaiming them on
    /// every write (unlike <c>FULL</c>, which would add overhead to every
    /// single transaction) or requiring a full <c>VACUUM</c>'s exclusive-
    /// lock whole-file rewrite to reclaim them later (unlike leaving
    /// <c>auto_vacuum</c> at its default of <c>NONE</c>). Call once at
    /// startup, right after migrations run.
    /// </summary>
    Task EnsureIncrementalAutoVacuumEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Reclaims every currently-free page back to the OS — cheap relative
    /// to a full <c>VACUUM</c> (no live data is rewritten, only the
    /// free-page list is walked and the file truncated), safe to call
    /// periodically against a live database. Also checkpoints afterward
    /// (see the implementation's own doc comment on why this database's
    /// WAL journal mode makes that necessary for the reclaimed space to
    /// actually show up on disk promptly). A no-op if nothing is actually
    /// free, or if <see cref="EnsureIncrementalAutoVacuumEnabledAsync"/>
    /// somehow hasn't run yet (SQLite just reports zero free pages to
    /// reclaim in that case, rather than erroring).
    /// </summary>
    Task IncrementalVacuumAsync(CancellationToken ct = default);
}
