using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.UpdateFilters;

public class UpdateFilterService(AppDbContext db, IAuditLogService auditLog, ILogger<UpdateFilterService> logger) : IUpdateFilterService
{
    /// <summary>
    /// Ships on the list by default (CLAUDE.md "Key configurable behaviors
    /// to preserve"). Contains no regex metacharacters, so it matches only
    /// this exact title until an admin edits it into something broader.
    /// </summary>
    public const string DefaultFilterName = "Security Intelligence-Update für Microsoft Defender Antivirus";

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        if (await db.UpdateFilters.AnyAsync(ct))
        {
            return;
        }

        db.UpdateFilters.Add(new UpdateFilter { Name = DefaultFilterName, Pattern = DefaultFilterName });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded the default update filter: {FilterName}", DefaultFilterName);
    }

    public async Task<IReadOnlyList<UpdateFilterDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.UpdateFilters
            .OrderBy(f => f.Name)
            .Select(f => new UpdateFilterDto(f.Id, f.Name, f.Pattern, f.CreatedAt))
            .ToListAsync(ct);

    public async Task<UpdateFilterResult> CreateAsync(UpsertUpdateFilterRequest request, string createdBy, CancellationToken ct = default)
    {
        var validationError = await ValidateAsync(request, existingId: null, ct);
        if (validationError is not null)
        {
            return UpdateFilterResult.Failed(validationError);
        }

        var filter = new UpdateFilter { Name = request.Name, Pattern = request.Pattern };
        db.UpdateFilters.Add(filter);
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(createdBy, "update-filter.create", filter.Name, ct);

        return UpdateFilterResult.Succeeded(new UpdateFilterDto(filter.Id, filter.Name, filter.Pattern, filter.CreatedAt));
    }

    public async Task<UpdateFilterResult> UpdateAsync(int id, UpsertUpdateFilterRequest request, string updatedBy, CancellationToken ct = default)
    {
        var filter = await db.UpdateFilters.SingleOrDefaultAsync(f => f.Id == id, ct);
        if (filter is null)
        {
            return UpdateFilterResult.Failed("Not found.");
        }

        var validationError = await ValidateAsync(request, existingId: id, ct);
        if (validationError is not null)
        {
            return UpdateFilterResult.Failed(validationError);
        }

        filter.Name = request.Name;
        filter.Pattern = request.Pattern;
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(updatedBy, "update-filter.update", filter.Name, ct);

        return UpdateFilterResult.Succeeded(new UpdateFilterDto(filter.Id, filter.Name, filter.Pattern, filter.CreatedAt));
    }

    public async Task<bool> DeleteAsync(int id, string deletedBy, CancellationToken ct = default)
    {
        var filter = await db.UpdateFilters.SingleOrDefaultAsync(f => f.Id == id, ct);
        if (filter is null)
        {
            return false;
        }

        db.UpdateFilters.Remove(filter);
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(deletedBy, "update-filter.delete", filter.Name, ct);

        return true;
    }

    private async Task<string?> ValidateAsync(UpsertUpdateFilterRequest request, int? existingId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Name must not be empty.";
        }

        if (string.IsNullOrWhiteSpace(request.Pattern))
        {
            return "Pattern must not be empty.";
        }

        try
        {
            // Compile-only check with the same timeout UpdateFilterMatcher
            // applies at match time — catches a syntactically invalid
            // pattern at save time rather than silently never matching
            // anything afterward.
            _ = new Regex(request.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException ex)
        {
            return $"Invalid regular expression: {ex.Message}";
        }

        var nameTaken = await db.UpdateFilters.AnyAsync(f => f.Name == request.Name && (existingId == null || f.Id != existingId), ct);
        if (nameTaken)
        {
            return "A filter with this name already exists.";
        }

        return null;
    }
}
