using System.Text.RegularExpressions;

namespace UpdateWatch2.Server.AgentUpdates;

/// <summary>
/// The three fixed release-asset kinds this project's own release pipeline
/// publishes (agent repo's <c>release.yml</c>) and the only ones ever
/// offered to an agent.
/// </summary>
public enum AgentUpdateAssetKind
{
    WindowsInstaller,
    LinuxDeb,
    LinuxRpm,
}

/// <summary>
/// Classifies a release asset filename by extension alone — shared by both
/// ways an asset reaches local storage: <see cref="AgentUpdateService"/>'s
/// GitHub download path and its manual-upload path (an admin uploading
/// release files directly through the admin UI, for a server that
/// deliberately has no internet access — see CLAUDE.md's "Agent
/// auto-update" bullet). Never inspects file content, only the name.
/// </summary>
public static class AgentUpdateAssetClassifier
{
    /// <summary>Null for anything that isn't one of the three known kinds (e.g. a GitHub release's own checksums.txt, or an admin uploading the wrong file).</summary>
    public static AgentUpdateAssetKind? Classify(string fileName)
    {
        if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return AgentUpdateAssetKind.WindowsInstaller;
        }

        if (fileName.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
        {
            return AgentUpdateAssetKind.LinuxDeb;
        }

        if (fileName.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase))
        {
            return AgentUpdateAssetKind.LinuxRpm;
        }

        return null;
    }
}

/// <summary>
/// Extracts the release version embedded in a manually uploaded asset's
/// filename, so <c>AgentUpdatesController.Upload</c> never has to trust an
/// admin-typed value that could mismatch what's actually in the file —
/// the original design had a free-text "Version" field alongside the file
/// picker, which this replaces at the user's request ("Die Version der
/// manuell hochgeladenen Agent-Binaries sollte besser aus dem Dateinamen
/// extrahiert werden. Das ist weniger fehleranfällig."). Every filename
/// this project's own release pipeline publishes carries its SemVer
/// version as a plain x.y.z run of digits
/// (<c>UpdateWatch2Agent-Setup-0.13.0-x64.exe</c>,
/// <c>updatewatch2-agent_0.13.0_amd64.deb</c>,
/// <c>updatewatch2-agent-0.13.0-1.x86_64.rpm</c>), so one generic pattern
/// covers all three known kinds rather than three separate exact-format
/// regexes tied to each package tool's own naming convention.
/// </summary>
public static partial class AgentUpdateVersionExtractor
{
    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionPattern();

    /// <summary>
    /// Null if no x.y.z run of digits is found in <paramref name="fileName"/>,
    /// or what's found doesn't parse as a <see cref="Version"/> — the same
    /// three-part-SemVer parsing this project already relies on elsewhere
    /// (e.g. <see cref="AgentUpdateService"/>'s own version comparison).
    /// </summary>
    public static string? Extract(string fileName)
    {
        var match = VersionPattern().Match(fileName);
        return match.Success && Version.TryParse(match.Value, out _) ? match.Value : null;
    }
}
