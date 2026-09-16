using System.Reflection;

namespace UpdateWatch2.Server.Admin;

/// <summary>
/// Renders a compact "what changed" summary between two
/// <see cref="AdminSettingsDto"/> snapshots for the audit log — reported by
/// a user directly ("Im Audit-Log steht oft nur 'admin.settings.updated'
/// und weitere Details fehlen. Die könnten noch den Unterschied von vorher
/// zu nachher widerspiegeln."). CLAUDE.md's audit-log requirement already
/// says every admin action is logged, but a bare action name with no
/// <c>Details</c> left an admin unable to tell what a given settings save
/// actually changed without a separate record of the previous values.
///
/// <para>
/// Reflection-based rather than a hand-maintained list of field names, so
/// a newly added settings field is covered automatically the moment it's
/// added to <see cref="AdminSettingsDto"/>, with nothing here needing a
/// matching edit. Safe to log every differing field's before/after value
/// as-is: <see cref="AdminSettingsDto"/> never carries a raw secret value
/// in the first place — <c>SmtpPassword</c>/<c>AdBindPassword</c>/
/// <c>GitHubToken</c> are represented only as <c>*Set</c> booleans on this
/// DTO — so there is nothing here that needs redacting before it's written
/// to the audit log.
/// </para>
/// </summary>
public static class AdminSettingsDiffFormatter
{
    private static readonly PropertyInfo[] Properties = typeof(AdminSettingsDto).GetProperties();

    /// <summary>Null when nothing actually changed (e.g. a save that round-trips the same values).</summary>
    public static string? Format(AdminSettingsDto before, AdminSettingsDto after)
    {
        var changes = Properties
            .Select(p => (p.Name, Before: p.GetValue(before), After: p.GetValue(after)))
            .Where(c => !Equals(c.Before, c.After))
            .Select(c => $"{c.Name}: {c.Before} -> {c.After}")
            .ToList();

        return changes.Count == 0 ? null : string.Join("; ", changes);
    }
}
