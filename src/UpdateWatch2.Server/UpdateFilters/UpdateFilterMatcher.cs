using System.Text.RegularExpressions;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.UpdateFilters;

/// <summary>
/// The one place that decides whether an update is excluded by the admin's
/// filter list — shared by <see cref="Updates.UpdateService"/> (the
/// pending-updates list itself) and <see cref="Agents.AgentService"/> (the
/// pending-update counts on the overview/detail pages), so both stay
/// consistent with each other and with whatever a future
/// threshold-crossing email notification uses.
/// </summary>
public static class UpdateFilterMatcher
{
    // Regex source is admin-entered, not attacker-controlled, but a timeout
    // is cheap insurance against a pattern that backtracks catastrophically
    // (e.g. a stray "(a+)+b") — one bad filter should never be able to hang
    // every subsequent updates-list request.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    public static bool IsExcluded(string title, IReadOnlyList<UpdateFilter> filters)
    {
        foreach (var filter in filters)
        {
            if (Matches(filter.Pattern, title))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True if <paramref name="pattern"/> is valid and matches
    /// <paramref name="input"/>. A pattern that fails to compile or times
    /// out is treated as a non-match rather than thrown — defensive
    /// against a stored pattern somehow going bad after
    /// <see cref="UpdateFilterService"/>'s own create/update validation
    /// already rejected it once; a single broken filter must never take
    /// down the whole updates display.
    /// </summary>
    public static bool Matches(string pattern, string input)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
