using UpdateWatch2.Server.AgentUpdates;

namespace UpdateWatch2.Server.Tests.TestHelpers;

/// <summary>
/// A minimal hand-written IGitHubReleaseClient fake — never makes a real
/// HTTP call. <see cref="DownloadAssetAsync"/> writes deterministic fake
/// content (the asset's own name) rather than anything resembling a real
/// binary, purely so AgentUpdateServiceTests can assert on the resulting
/// file/hash without needing a real download.
/// </summary>
public class FakeGitHubReleaseClient : IGitHubReleaseClient
{
    public GitHubRelease? Release { get; set; }

    public Exception? ThrowOnGetLatestRelease { get; set; }

    public List<string> RequestedTokens { get; } = [];

    public Task<GitHubRelease?> GetLatestReleaseAsync(string owner, string repo, string? token, CancellationToken ct = default)
    {
        if (token is not null)
        {
            RequestedTokens.Add(token);
        }

        return ThrowOnGetLatestRelease is not null
            ? throw ThrowOnGetLatestRelease
            : Task.FromResult(Release);
    }

    public async Task<(string Sha256, long SizeBytes)> DownloadAssetAsync(string downloadUrl, string destinationPath, CancellationToken ct = default)
    {
        var content = $"fake-content-for:{downloadUrl}";
        await File.WriteAllTextAsync(destinationPath, content, ct);
        var sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
        return (sha256, content.Length);
    }
}
