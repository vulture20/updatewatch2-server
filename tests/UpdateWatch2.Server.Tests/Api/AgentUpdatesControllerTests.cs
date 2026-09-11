using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Api;

/// <summary>
/// Covers only the auth gate plus the read-only status shape for the
/// "check" endpoint, deliberately not its success path — same reasoning
/// as <c>CertificateAuthorityEndpointTests</c>' own doc comment: unlike
/// every other endpoint under <c>tests/.../Api/</c>, actually calling it
/// would exercise the real <c>AgentUpdateService</c> against the live
/// GitHub API (<c>AgentAutoUpdateEnabled</c> defaults to <c>true</c> for a
/// freshly seeded admin-settings row, and there's no config-level way to
/// override a DB-backed setting from here), which is exactly the mistake
/// <c>WithoutBackgroundWorkers</c>'s own doc comment describes this
/// project once shipping for real by accident. The actual check/download
/// logic is covered by <c>AgentUpdateServiceTests</c> against a fake
/// <c>IGitHubReleaseClient</c> instead. The "upload" endpoint has no such
/// restriction — it never talks to GitHub at all — so its success path
/// (and validation) is exercised for real here, over an actual HTTP
/// multipart request against the real <c>AgentUpdateService</c>.
/// </summary>
public class AgentUpdatesControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;
    private HttpClient _anonymousClient = null!;

    public AgentUpdatesControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.WithoutBackgroundWorkers().ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Path"] = _dbPath,
                })));
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _anonymousClient = _factory.CreateClient();
        await AuthTestHelper.SeedAdminAsync(_factory.Services);
        await AuthTestHelper.LoginAsync(_client);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _anonymousClient.Dispose();
        File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Status_requires_an_admin_session()
    {
        var response = await _anonymousClient.GetAsync("/api/admin/agent-update-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Manual_check_requires_an_admin_session()
    {
        var response = await _anonymousClient.PostAsync("/api/admin/agent-update-status/check", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Status_reports_nothing_known_yet_by_default()
    {
        var response = await _client.GetAsync("/api/admin/agent-update-status");

        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<StatusDto>();
        Assert.NotNull(status);
        Assert.True(status!.enabled);
        Assert.Null(status.latestVersion);
        Assert.Null(status.checkedAt);
        Assert.Null(status.lastError);
        Assert.False(status.manuallyUploaded);
    }

    [Fact]
    public async Task Upload_requires_an_admin_session()
    {
        using var response = await _anonymousClient.PostAsync("/api/admin/agent-update-status/upload", MakeUploadForm(("updatewatch2-agent_0.13.0_amd64.deb", "deb-bytes")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_a_file_whose_version_cant_be_determined_from_its_name()
    {
        using var response = await _client.PostAsync("/api/admin/agent-update-status/upload", MakeUploadForm(("updatewatch2-agent_amd64.deb", "deb-bytes")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_files_from_different_versions()
    {
        using var response = await _client.PostAsync(
            "/api/admin/agent-update-status/upload",
            MakeUploadForm(
                ("updatewatch2-agent_0.13.0_amd64.deb", "deb-bytes"),
                ("UpdateWatch2Agent-Setup-0.14.0-x64.exe", "exe-bytes")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_a_request_with_no_files()
    {
        using var form = new MultipartFormDataContent();

        using var response = await _client.PostAsync("/api/admin/agent-update-status/upload", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_an_unrecognized_file_extension()
    {
        using var response = await _client.PostAsync("/api/admin/agent-update-status/upload", MakeUploadForm(("checksums.txt", "not a real package")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_extracts_the_version_from_the_filename_and_it_becomes_the_new_status()
    {
        using var response = await _client.PostAsync(
            "/api/admin/agent-update-status/upload",
            MakeUploadForm(("updatewatch2-agent_0.13.0_amd64.deb", "deb-bytes")));

        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<StatusDto>();
        Assert.NotNull(status);
        Assert.Equal("0.13.0", status!.latestVersion);
        Assert.True(status.manuallyUploaded);

        var statusResponse = await _client.GetAsync("/api/admin/agent-update-status");
        var reread = await statusResponse.Content.ReadFromJsonAsync<StatusDto>();
        Assert.Equal("0.13.0", reread!.latestVersion);
    }

    private static MultipartFormDataContent MakeUploadForm(params (string FileName, string Content)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (fileName, content) in files)
        {
            var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
            form.Add(fileContent, "files", fileName);
        }

        return form;
    }

    private record StatusDto(bool enabled, string? latestVersion, DateTimeOffset? checkedAt, string? lastError, bool manuallyUploaded);
}
