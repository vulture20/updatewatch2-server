using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace UpdateWatch2.Server.Db;

/// <summary>
/// Uses its own fresh <see cref="SqliteConnection"/> (built from
/// <see cref="AppDbContext"/>'s own connection string, the same way
/// <c>Program.cs</c>'s <c>TryReadPersistedLogLevel</c> already opens a
/// standalone connection for its own one-off read) rather than reusing
/// <see cref="AppDbContext"/>'s EF-managed one — <c>VACUUM</c> needs a
/// connection with no other statements or transactions in flight, which a
/// dedicated, short-lived connection guarantees without having to reason
/// about whatever else the shared EF connection might be doing.
///
/// <para>
/// <b>This database runs in WAL journal mode</b> (confirmed by hand — not
/// something this project explicitly configures anywhere, it's simply
/// Microsoft.Data.Sqlite's own default), which matters a lot here: every
/// write, <em>including</em> what <c>VACUUM</c>/<c>incremental_vacuum</c>
/// themselves do, lands in the <c>-wal</c> sidecar file first, not the
/// main <c>.sqlite</c> file — confirmed with a throwaway harness that a
/// bare <c>VACUUM</c> with no follow-up checkpoint left the main file
/// exactly as large as before, with the WAL file having grown instead.
/// Without an explicit <c>PRAGMA wal_checkpoint(TRUNCATE)</c> afterward,
/// the reclaimed space would still exist, just sitting in a WAL file that
/// itself only gets folded back into the main file whenever SQLite's own
/// automatic checkpoint threshold happens to trigger (every ~1000 pages
/// by default) — for this project's realistically low write volume, that
/// could take a very long time, defeating the entire point of this
/// service. Both methods below force the checkpoint explicitly so the
/// space is actually returned to the OS promptly, not just logically
/// available whenever SQLite eventually gets around to it.
/// </para>
/// </summary>
public class DatabaseMaintenanceService(AppDbContext db, ILogger<DatabaseMaintenanceService> logger) : IDatabaseMaintenanceService
{
    // SQLite's own auto_vacuum pragma values — see
    // https://sqlite.org/pragma.html#pragma_auto_vacuum. 0 = NONE
    // (SQLite's own default), 1 = FULL, 2 = INCREMENTAL.
    private const long IncrementalAutoVacuumMode = 2;

    public async Task EnsureIncrementalAutoVacuumEnabledAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);

        var current = await ExecuteScalarAsync(connection, "PRAGMA auto_vacuum;", ct);
        if (Convert.ToInt64(current) == IncrementalAutoVacuumMode)
        {
            // Already converted on a previous startup — nothing to do.
            return;
        }

        // Setting the pragma alone doesn't restructure an already
        // non-empty database — SQLite only actually applies a switch INTO
        // incremental/full mode on the next VACUUM, which is therefore
        // required here, once, to convert the file layout. Every startup
        // after this one reads auto_vacuum as already == INCREMENTAL above
        // and returns before ever reaching this block again.
        logger.LogInformation("Enabling incremental auto-vacuum for the SQLite database — running a one-time VACUUM to convert the file layout.");
        await ExecuteNonQueryAsync(connection, "PRAGMA auto_vacuum = INCREMENTAL;", ct);
        await ExecuteNonQueryAsync(connection, "VACUUM;", ct);
        await ExecuteNonQueryAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);", ct);
    }

    public async Task IncrementalVacuumAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);

        // No argument: reclaim every currently-free page, not just a
        // bounded batch — cheap enough for this project's realistic
        // database sizes that there's no need for the added complexity of
        // picking and looping over a page-count limit.
        await ExecuteNonQueryAsync(connection, "PRAGMA incremental_vacuum;", ct);
        await ExecuteNonQueryAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);", ct);
    }

    private static async Task<object?> ExecuteScalarAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(ct);
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
