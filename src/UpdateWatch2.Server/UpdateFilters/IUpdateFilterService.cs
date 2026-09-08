namespace UpdateWatch2.Server.UpdateFilters;

public interface IUpdateFilterService
{
    /// <summary>
    /// Seeds the default "Security Intelligence-Update für Microsoft
    /// Defender Antivirus" filter (CLAUDE.md "Key configurable behaviors to
    /// preserve") if the table is completely empty — a fresh install ships
    /// with it on the list; once an admin has the table in any state
    /// (including having deleted every row, including that one), it's
    /// never re-seeded. Called once at startup, the same
    /// seed-only-if-empty convention <c>Demo.DemoDataSeeder</c> already
    /// uses.
    /// </summary>
    Task EnsureSeededAsync(CancellationToken ct = default);

    Task<IReadOnlyList<UpdateFilterDto>> GetAllAsync(CancellationToken ct = default);

    Task<UpdateFilterResult> CreateAsync(UpsertUpdateFilterRequest request, string createdBy, CancellationToken ct = default);

    Task<UpdateFilterResult> UpdateAsync(int id, UpsertUpdateFilterRequest request, string updatedBy, CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, string deletedBy, CancellationToken ct = default);
}
