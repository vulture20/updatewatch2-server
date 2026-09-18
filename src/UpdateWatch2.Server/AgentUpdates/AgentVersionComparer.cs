namespace UpdateWatch2.Server.AgentUpdates;

/// <summary>
/// The one shared "is this self-reported agent version older than a given
/// reference version" decision point — used both by
/// <see cref="AgentUpdateService"/>'s own up-to-date check (deciding
/// whether to offer a self-update) and <see cref="Agents.AgentService.GetAllAsync"/>
/// (deciding whether to flag an outdated agent in the overview list), so
/// the two can never disagree about what counts as outdated. The same
/// testable-shared-decision-point pattern this codebase already uses
/// elsewhere (e.g. <see cref="UpdateFilters.UpdateFilterMatcher"/>).
/// </summary>
public static class AgentVersionComparer
{
    /// <summary>
    /// True iff both versions parse and <paramref name="version"/> is
    /// strictly older than <paramref name="referenceVersion"/>. A missing
    /// or unparsable version on either side is treated as "not older" —
    /// errs toward not flagging/offering anything for a build too old (or
    /// too new/unknown) to compare reliably, mirroring
    /// <c>AgentUpdateService.GetOfferForAsync</c>'s own long-standing
    /// reasoning for the identical comparison.
    /// </summary>
    public static bool IsOlderThan(string? version, string? referenceVersion)
    {
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(referenceVersion))
        {
            return false;
        }

        // System.Version parses a bare "0.10.0"-style SemVer string fine
        // (three-part Major.Minor.Build) — this project's agent version
        // never uses a pre-release suffix, so no dedicated SemVer parser
        // is needed here.
        if (!Version.TryParse(version, out var parsedVersion) || !Version.TryParse(referenceVersion, out var parsedReference))
        {
            return false;
        }

        return parsedVersion < parsedReference;
    }
}
