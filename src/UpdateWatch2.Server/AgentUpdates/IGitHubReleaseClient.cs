namespace UpdateWatch2.Server.AgentUpdates;

/// <summary>
/// Talks to GitHub's Releases API and downloads release assets —
/// abstracted behind an interface purely for testability, the same
/// seam pattern <c>IServerClient</c> plays on the agent side: a fake
/// implementation drives <see cref="AgentUpdateService"/>'s own tests
/// without ever making a real HTTP call.
/// </summary>
public interface IGitHubReleaseClient
{
    /// <summary>
    /// Null if the repo has no published (non-draft, non-prerelease)
    /// release yet — GitHub's own <c>/releases/latest</c> semantics,
    /// surfaced here as null rather than an exception since "no release
    /// yet" isn't a failure.
    /// </summary>
    Task<GitHubRelease?> GetLatestReleaseAsync(string owner, string repo, string? token, CancellationToken ct = default);

    /// <summary>Downloads the asset at <paramref name="downloadUrl"/> to <paramref name="destinationPath"/>, returning its SHA-256 (lowercase hex) and size in bytes.</summary>
    Task<(string Sha256, long SizeBytes)> DownloadAssetAsync(string downloadUrl, string destinationPath, CancellationToken ct = default);
}
