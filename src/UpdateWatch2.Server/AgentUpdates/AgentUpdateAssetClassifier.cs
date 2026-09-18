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
/// The two architectures this project's own release pipeline publishes for
/// every <see cref="AgentUpdateAssetKind"/> (updatewatch2-agent#22/#23) —
/// orthogonal to <see cref="AgentUpdateAssetKind"/>, not folded into it:
/// <see cref="AgentUpdateAssetKind"/> alone still fully determines which
/// package FORMAT/command applies (dpkg vs. rpm vs. the Windows installer),
/// something several call sites (agent-side <c>LinuxPackageApplier</c>,
/// this project's own DI wiring choosing which <c>IPlatformUpdateApplier</c>
/// to construct) only ever cared about and never needed to know the
/// architecture for — expanding <see cref="AgentUpdateAssetKind"/> itself
/// to six values instead would have forced every one of those call sites to
/// learn about architecture too, for no reason.
/// </summary>
public enum AgentUpdateAssetArch
{
    X64,
    Arm64,
}

/// <summary>
/// Classifies a release asset filename by extension AND architecture
/// suffix — shared by both ways an asset reaches local storage:
/// <see cref="AgentUpdateService"/>'s GitHub download path and its
/// manual-upload path (an admin uploading release files directly through
/// the admin UI, for a server that deliberately has no internet access —
/// see CLAUDE.md's "Agent auto-update" bullet). Never inspects file
/// content, only the name.
///
/// <para>
/// Architecture-aware since updatewatch2-agent#22/#23 added a second
/// architecture for every kind — before that, a release only ever
/// published exactly one file per kind, so classifying by extension alone
/// was enough. Left unfixed, two files of the same kind (e.g. the x64 and
/// arm64 Windows installers) would classify identically and the second one
/// processed would silently overwrite the first in
/// <see cref="AgentUpdateService"/>'s single-slot-per-kind storage — found
/// by a direct user question asking whether self-update had actually been
/// considered for the new multi-arch releases at all (it hadn't, when this
/// was first asked).
/// </para>
///
/// <para>
/// Matches this project's own exact, unchanging filename conventions —
/// <c>UpdateWatch2Agent-Setup-&lt;version&gt;-{x64,arm64}.exe</c>,
/// <c>updatewatch2-agent_&lt;version&gt;_{amd64,arm64}.deb</c>,
/// <c>updatewatch2-agent-&lt;version&gt;-1.{x86_64,aarch64}.rpm</c> — every
/// historical release already carried an explicit architecture suffix (the
/// installer/package filenames were never architecture-ambiguous, even
/// before a second architecture existed to publish), so this is fully
/// backward compatible with every asset this project has ever published,
/// not just future ones.
/// </para>
/// </summary>
public static class AgentUpdateAssetClassifier
{
    /// <summary>Null for anything that isn't one of the six known kind/architecture combinations (e.g. a GitHub release's own checksums.txt, an admin uploading the wrong file, or a filename missing its architecture suffix).</summary>
    public static (AgentUpdateAssetKind Kind, AgentUpdateAssetArch Arch)? Classify(string fileName)
    {
        if (fileName.EndsWith("-x64.exe", StringComparison.OrdinalIgnoreCase))
        {
            return (AgentUpdateAssetKind.WindowsInstaller, AgentUpdateAssetArch.X64);
        }

        if (fileName.EndsWith("-arm64.exe", StringComparison.OrdinalIgnoreCase))
        {
            return (AgentUpdateAssetKind.WindowsInstaller, AgentUpdateAssetArch.Arm64);
        }

        if (fileName.EndsWith("_amd64.deb", StringComparison.OrdinalIgnoreCase))
        {
            return (AgentUpdateAssetKind.LinuxDeb, AgentUpdateAssetArch.X64);
        }

        if (fileName.EndsWith("_arm64.deb", StringComparison.OrdinalIgnoreCase))
        {
            return (AgentUpdateAssetKind.LinuxDeb, AgentUpdateAssetArch.Arm64);
        }

        if (fileName.EndsWith(".x86_64.rpm", StringComparison.OrdinalIgnoreCase))
        {
            return (AgentUpdateAssetKind.LinuxRpm, AgentUpdateAssetArch.X64);
        }

        if (fileName.EndsWith(".aarch64.rpm", StringComparison.OrdinalIgnoreCase))
        {
            return (AgentUpdateAssetKind.LinuxRpm, AgentUpdateAssetArch.Arm64);
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
