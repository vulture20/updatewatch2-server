using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.UpdateFilters;

namespace UpdateWatch2.Server.Tests.UpdateFilters;

public class UpdateFilterServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-update-filter-service-test-{Guid.NewGuid()}.sqlite");
    private readonly AppDbContext _db;
    private readonly UpdateFilterService _service;

    public UpdateFilterServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();
        _service = new UpdateFilterService(_db, new AuditLogService(_db), NullLogger<UpdateFilterService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task EnsureSeededAsync_adds_the_default_filter_to_an_empty_table()
    {
        await _service.EnsureSeededAsync();

        var filter = await _db.UpdateFilters.SingleAsync();
        Assert.Equal(UpdateFilterService.DefaultFilterName, filter.Name);
        Assert.Equal(UpdateFilterService.DefaultFilterName, filter.Pattern);
    }

    [Fact]
    public async Task EnsureSeededAsync_is_a_no_op_once_the_table_has_any_row_including_after_the_default_was_deleted()
    {
        // Simulates an admin who deleted the seeded default — it must never
        // come back, so "any row at all" is the seeded check, not "does the
        // specific default filter exist".
        _db.UpdateFilters.Add(new UpdateFilter { Name = "Custom only", Pattern = "foo" });
        await _db.SaveChangesAsync();

        await _service.EnsureSeededAsync();

        var names = await _db.UpdateFilters.Select(f => f.Name).ToListAsync();
        Assert.Equal(["Custom only"], names);
    }

    [Fact]
    public async Task CreateAsync_adds_a_filter_and_writes_an_audit_log_entry()
    {
        var result = await _service.CreateAsync(new UpsertUpdateFilterRequest("Edge", "Microsoft Edge"), createdBy: "alice");

        Assert.True(result.Success);
        Assert.Equal("Edge", result.Filter!.Name);
        var entry = await _db.AuditLogEntries.SingleAsync(e => e.Action == "update-filter.create");
        Assert.Equal("alice", entry.Actor);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_empty_name()
    {
        var result = await _service.CreateAsync(new UpsertUpdateFilterRequest("", "foo"), createdBy: "admin");

        Assert.False(result.Success);
        Assert.Empty(await _db.UpdateFilters.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_an_invalid_regex()
    {
        var result = await _service.CreateAsync(new UpsertUpdateFilterRequest("Broken", "("), createdBy: "admin");

        Assert.False(result.Success);
        Assert.Contains("Invalid regular expression", result.FailureReason);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_name()
    {
        await _service.CreateAsync(new UpsertUpdateFilterRequest("Edge", "Microsoft Edge"), createdBy: "admin");

        var result = await _service.CreateAsync(new UpsertUpdateFilterRequest("Edge", "something else"), createdBy: "admin");

        Assert.False(result.Success);
        Assert.Single(await _db.UpdateFilters.ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_changes_name_and_pattern_and_writes_an_audit_log_entry()
    {
        var created = await _service.CreateAsync(new UpsertUpdateFilterRequest("Edge", "Microsoft Edge"), createdBy: "admin");

        var result = await _service.UpdateAsync(created.Filter!.Id, new UpsertUpdateFilterRequest("Edge (renamed)", "Edge Update"), updatedBy: "bob");

        Assert.True(result.Success);
        Assert.Equal("Edge (renamed)", result.Filter!.Name);
        Assert.Equal("Edge Update", result.Filter.Pattern);
        var entry = await _db.AuditLogEntries.SingleAsync(e => e.Action == "update-filter.update");
        Assert.Equal("bob", entry.Actor);
    }

    [Fact]
    public async Task UpdateAsync_allows_keeping_the_same_name_on_the_filter_being_updated()
    {
        var created = await _service.CreateAsync(new UpsertUpdateFilterRequest("Edge", "Microsoft Edge"), createdBy: "admin");

        var result = await _service.UpdateAsync(created.Filter!.Id, new UpsertUpdateFilterRequest("Edge", "Microsoft Edge Update"), updatedBy: "admin");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task UpdateAsync_fails_with_Not_found_for_an_unknown_id()
    {
        var result = await _service.UpdateAsync(999, new UpsertUpdateFilterRequest("x", "y"), updatedBy: "admin");

        Assert.False(result.Success);
        Assert.Equal("Not found.", result.FailureReason);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_filter_and_writes_an_audit_log_entry()
    {
        var created = await _service.CreateAsync(new UpsertUpdateFilterRequest("Edge", "Microsoft Edge"), createdBy: "admin");

        var deleted = await _service.DeleteAsync(created.Filter!.Id, deletedBy: "alice");

        Assert.True(deleted);
        Assert.Empty(await _db.UpdateFilters.ToListAsync());
        var entry = await _db.AuditLogEntries.SingleAsync(e => e.Action == "update-filter.delete");
        Assert.Equal("alice", entry.Actor);
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_an_unknown_id()
    {
        var deleted = await _service.DeleteAsync(999, deletedBy: "admin");

        Assert.False(deleted);
    }
}
