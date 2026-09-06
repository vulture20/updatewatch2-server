namespace UpdateWatch2.Server.AgentUpdates;

/// <summary>One asset attached to a GitHub release — the simplified, non-GitHub-specific shape the rest of this codebase works with.</summary>
public record GitHubReleaseAsset(string Name, string BrowserDownloadUrl, long Size);

/// <summary>A GitHub release, trimmed to just the fields <see cref="IAgentUpdateService"/> needs.</summary>
public record GitHubRelease(string TagName, IReadOnlyList<GitHubReleaseAsset> Assets);
