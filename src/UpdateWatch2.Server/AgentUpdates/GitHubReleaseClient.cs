using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace UpdateWatch2.Server.AgentUpdates;

/// <summary>
/// Real implementation of <see cref="IGitHubReleaseClient"/>, using the
/// typed <see cref="HttpClient"/> Program.cs registers (base address
/// <c>https://api.github.com/</c>, a required User-Agent header — GitHub
/// rejects API requests with none — and the recommended
/// <c>application/vnd.github+json</c> Accept header). The optional
/// per-call token is applied as a bearer credential rather than baked
/// into the client's default headers, since it can change at runtime
/// (an admin editing it via <c>PUT /api/admin/settings</c>) without a
/// service restart, the same live-reload behavior every other admin
/// setting already gets.
/// </summary>
public class GitHubReleaseClient(HttpClient httpClient) : IGitHubReleaseClient
{
    public async Task<GitHubRelease?> GetLatestReleaseAsync(string owner, string repo, string? token, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{owner}/{repo}/releases/latest");
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // No releases published for this repo yet — not an error.
            return null;
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GitHubReleaseResponse>(cancellationToken: ct);
        return payload is null
            ? null
            : new GitHubRelease(
                payload.TagName,
                payload.Assets.Select(a => new GitHubReleaseAsset(a.Name, a.BrowserDownloadUrl, a.Size)).ToList());
    }

    public async Task<(string Sha256, long SizeBytes)> DownloadAssetAsync(string downloadUrl, string destinationPath, CancellationToken ct = default)
    {
        // downloadUrl is an absolute URL on a different host (github.com's
        // asset CDN, not api.github.com) — HttpClient honors an absolute
        // URI on a per-request basis regardless of the client's own
        // BaseAddress, so reusing this same typed client is safe.
        using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var destination = File.Create(destinationPath);
        using var sha256 = SHA256.Create();
        await using (var hashingStream = new CryptoStream(destination, sha256, CryptoStreamMode.Write, leaveOpen: true))
        {
            await source.CopyToAsync(hashingStream, ct);
        }

        var size = destination.Length;
        return (Convert.ToHexStringLower(sha256.Hash!), size);
    }

    private record GitHubReleaseResponse(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAssetResponse> Assets);

    private record GitHubReleaseAssetResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("size")] long Size);
}
