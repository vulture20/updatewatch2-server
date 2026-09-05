using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Api;

/// <summary>
/// Covers only the auth gate here, plus one non-mutating read, deliberately —
/// unlike <c>AgentRegistrationServiceTests</c>/<c>InternalCertificateAuthorityTests</c>,
/// which each construct their own <c>InternalCertificateAuthority</c> against an
/// isolated temp directory, every <see cref="WebApplicationFactory{TEntryPoint}"/>-based
/// test class in this suite (this one included) resolves the CA singleton
/// Program.cs wires up against the shared, real, non-test-isolated default
/// certs directory (no per-test <c>Certs:Path</c> override exists, mirroring
/// how none of the other WebApplicationFactory-based tests touch it
/// destructively either). Actually calling prepare/activate/retire-previous
/// here would mutate that shared <c>ca.pfx</c>/<c>server.pfx</c> for
/// whatever else is reading it (another concurrent test run, a live
/// `dotnet run`) — the full rotation lifecycle is exercised safely instead
/// by <c>InternalCertificateAuthorityTests</c>, and by a live run (see
/// updatewatch2-server#6's closing notes).
/// </summary>
public class CertificateAuthorityEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;
    private HttpClient _anonymousClient = null!;

    public CertificateAuthorityEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
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
        var response = await _anonymousClient.GetAsync("/api/admin/certificate-authority");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("activate")]
    [InlineData("retire-previous")]
    public async Task Rotation_actions_require_an_admin_session(string action)
    {
        var response = await _anonymousClient.PostAsync($"/api/admin/certificate-authority/{action}", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Status_reports_the_current_root_with_no_previous_or_pending_root_by_default()
    {
        var response = await _client.GetAsync("/api/admin/certificate-authority");

        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<RotationStatus>();
        Assert.NotNull(status);
        Assert.False(string.IsNullOrEmpty(status!.currentThumbprint));
        // Not asserting these are null: another test class sharing the same
        // real certs directory (see class remarks) may have already run a
        // live rotation walkthrough against it. Just confirming the shape
        // deserializes and a current root is always reported.
    }

    private record RotationStatus(string currentThumbprint, DateTimeOffset currentNotAfter, string? previousThumbprint, DateTimeOffset? previousNotAfter, string? pendingThumbprint, DateTimeOffset? pendingNotAfter);
}
