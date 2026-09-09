using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Api;

/// <summary>
/// Covers the auth gate plus the read shape here — actually exercising a
/// real rejected mTLS handshake needs a live TLS connection, which
/// <see cref="WebApplicationFactory{TEntryPoint}"/>'s in-memory
/// <c>TestServer</c> can't do (same limitation CLAUDE.md already documents
/// for every other mTLS-adjacent test in this suite); the reason
/// classification and audit-log recording this endpoint reads back are
/// covered directly by <c>CertificateRejectionClassifierTests</c>/
/// <c>CertificateRejectionServiceTests</c> instead.
/// </summary>
public class CertificateRejectionsControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;
    private HttpClient _anonymousClient = null!;

    public CertificateRejectionsControllerTests(WebApplicationFactory<Program> factory)
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
    public async Task Get_requires_an_admin_session()
    {
        var response = await _anonymousClient.GetAsync("/api/admin/certificate-rejections");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_reports_no_rejections_by_default()
    {
        var response = await _client.GetAsync("/api/admin/certificate-rejections");

        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<CertificateRejectionStatusDto>();
        Assert.NotNull(status);
        Assert.Equal(0, status!.RecentCount);
        Assert.Empty(status.Recent);
    }

    [Fact]
    public async Task Get_reflects_a_rejection_recorded_through_the_service()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var rejectionService = scope.ServiceProvider.GetRequiredService<ICertificateRejectionService>();
            await rejectionService.RecordAsync(CertificateRejectionReason.Expired, certificate: null, remoteIpAddress: "203.0.113.7");
        }

        var response = await _client.GetAsync("/api/admin/certificate-rejections");

        var status = await response.Content.ReadFromJsonAsync<CertificateRejectionStatusDto>();
        Assert.Equal(1, status!.RecentCount);
        Assert.Equal("Expired", status.Recent[0].Reason);
    }

    [Fact]
    public async Task Acknowledge_requires_an_admin_session()
    {
        var response = await _anonymousClient.PostAsync("/api/admin/certificate-rejections/acknowledge", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Acknowledge_silences_the_banner_and_returns_the_refreshed_status()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var rejectionService = scope.ServiceProvider.GetRequiredService<ICertificateRejectionService>();
            await rejectionService.RecordAsync(CertificateRejectionReason.Expired, certificate: null, remoteIpAddress: null);
        }

        var response = await _client.PostAsync("/api/admin/certificate-rejections/acknowledge", content: null);

        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<CertificateRejectionStatusDto>();
        Assert.Equal(0, status!.RecentCount);

        var followUp = await _client.GetAsync("/api/admin/certificate-rejections");
        var followUpStatus = await followUp.Content.ReadFromJsonAsync<CertificateRejectionStatusDto>();
        Assert.Equal(0, followUpStatus!.RecentCount);
    }
}
