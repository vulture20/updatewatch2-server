using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Tests.Db;

/// <summary>
/// Runs against a real SQLite file (not mocked) — the actual behavior
/// under test (auto_vacuum pragma switching, page reclamation) only
/// exists in SQLite itself, not in anything EF Core could fake.
/// </summary>
public class DatabaseMaintenanceServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-db-maintenance-test-{Guid.NewGuid()}.sqlite");
    private readonly AppDbContext _db;
    private readonly DatabaseMaintenanceService _service;

    public DatabaseMaintenanceServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();
        _service = new DatabaseMaintenanceService(_db, NullLogger<DatabaseMaintenanceService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task EnsureIncrementalAutoVacuumEnabledAsync_switches_the_database_to_incremental_mode()
    {
        Assert.Equal(0, await ReadAutoVacuumModeAsync()); // SQLite's own default: NONE

        await _service.EnsureIncrementalAutoVacuumEnabledAsync();

        Assert.Equal(2, await ReadAutoVacuumModeAsync()); // INCREMENTAL
    }

    [Fact]
    public async Task EnsureIncrementalAutoVacuumEnabledAsync_is_idempotent()
    {
        await _service.EnsureIncrementalAutoVacuumEnabledAsync();
        // A second call must not throw (e.g. from re-running VACUUM
        // unnecessarily or misreading the now-already-INCREMENTAL mode)
        // — this is exactly what every startup after the first sees.
        await _service.EnsureIncrementalAutoVacuumEnabledAsync();

        Assert.Equal(2, await ReadAutoVacuumModeAsync());
    }

    [Fact]
    public async Task IncrementalVacuumAsync_reclaims_space_freed_by_deleted_rows_without_losing_surviving_data()
    {
        await _service.EnsureIncrementalAutoVacuumEnabledAsync();

        var keep = new AuditLogEntry { Actor = "admin", Action = "kept.entry", Details = "still here" };
        _db.AuditLogEntries.Add(keep);
        for (var i = 0; i < 500; i++)
        {
            // Meaningful bulk of content so deleting it frees whole pages,
            // not just a fraction of one — a too-small test dataset here
            // wouldn't reliably demonstrate anything shrunk at all.
            _db.AuditLogEntries.Add(new AuditLogEntry { Actor = "admin", Action = "churn.entry", Details = new string('x', 500) });
        }
        await _db.SaveChangesAsync();

        _db.AuditLogEntries.RemoveRange(_db.AuditLogEntries.Where(e => e.Action == "churn.entry"));
        await _db.SaveChangesAsync();

        var sizeBeforeReclaim = TotalOnDiskSize();
        await _service.IncrementalVacuumAsync();
        var sizeAfterReclaim = TotalOnDiskSize();

        Assert.True(sizeAfterReclaim < sizeBeforeReclaim, $"Expected the on-disk footprint to shrink after reclaiming freed pages ({sizeBeforeReclaim} -> {sizeAfterReclaim}).");

        var reloaded = await _db.AuditLogEntries.SingleAsync();
        Assert.Equal("kept.entry", reloaded.Action);
        Assert.Equal("still here", reloaded.Details);
    }

    /// <summary>
    /// Sums the main <c>.sqlite</c> file plus its <c>-wal</c>/<c>-shm</c>
    /// sidecar files (present because this database runs in WAL mode —
    /// see <see cref="DatabaseMaintenanceService"/>'s own doc comment) —
    /// what actually matters is the *combined* on-disk footprint, not the
    /// main file alone, since a write (including what <c>VACUUM</c>/
    /// <c>incremental_vacuum</c> themselves do) lands in the WAL file
    /// first regardless of whether the reclaiming step that produced it
    /// already checkpointed.
    /// </summary>
    private long TotalOnDiskSize() =>
        new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" }
            .Where(File.Exists)
            .Sum(f => new FileInfo(f).Length);

    private async Task<long> ReadAutoVacuumModeAsync()
    {
        await using var connection = new SqliteConnection(_db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA auto_vacuum;";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
