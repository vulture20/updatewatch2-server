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
