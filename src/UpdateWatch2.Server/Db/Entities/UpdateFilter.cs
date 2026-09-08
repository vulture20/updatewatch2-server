namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// An admin-defined regex filter (CLAUDE.md "Key configurable behaviors to
/// preserve" — global update filters). Any <see cref="UpdateItem"/> whose
/// Title matches any row here is excluded from the pending-updates display
/// (<see cref="Updates.UpdateService.GetForAgentAsync"/>,
/// <see cref="Agents.AgentService"/>'s pending-update counts) and, once the
/// threshold-crossing email notification itself is implemented, from that
/// too — see <see cref="UpdateFilters.UpdateFilterMatcher"/>, the one place
/// that decision is made. Matching is evaluated live against the current
/// set of filters on every read, not cached at report time, so adding or
/// editing a filter immediately changes what's shown — no re-report or
/// restart needed.
/// </summary>
public class UpdateFilter
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>.NET regex pattern, matched case-insensitively against UpdateItem.Title.</summary>
    public required string Pattern { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
