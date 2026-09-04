using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Api;

public class ApiEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;

    public ApiEndpointTests(WebApplicationFactory<Program> factory)
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
        await AuthTestHelper.SeedAdminAsync(_factory.Services);
        await AuthTestHelper.LoginAsync(_client);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Version_returns_all_four_version_numbers()
    {
        var response = await _client.GetFromJsonAsync<VersionResponse>("/api/version");

        Assert.NotNull(response);
        Assert.Equal("0.9.0", response.server);
        Assert.Equal("0.3.0", response.protocol);
        Assert.Equal("0.6.0", response.database);
    }

    [Fact]
    public async Task Unknown_agent_returns_not_found()
    {
        var response = await _client.GetAsync("/api/agents/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Agents_list_starts_empty()
    {
        var agents = await _client.GetFromJsonAsync<List<object>>("/api/agents");

        Assert.NotNull(agents);
        Assert.Empty(agents);
    }

    [Fact]
    public async Task Agents_endpoints_reject_unauthenticated_requests()
    {
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync("/api/agents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Renew_rejects_a_request_with_no_client_certificate()
    {
        // WebApplicationFactory's in-memory TestServer bypasses Kestrel/real
        // TLS entirely, so this can only prove the endpoint is gated at all
        // — a full successful-renewal-over-real-mTLS case needs a live run
        // (see CLAUDE.md's note on the same limitation for the original
        // mTLS work). Certificate authentication has no meaningful
        // "challenge" (you can't ask a client to retroactively present a
        // TLS client cert after the handshake), so a missing certificate
        // fails as 403 Forbidden, not 401 Unauthorized — confirmed by
        // running this test, not assumed.
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.PostAsync("/api/agents/some-host/renew", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReissueCertificate_requires_an_admin_session()
    {
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.PostAsync("/api/agents/some-host/reissue-certificate", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReissueCertificate_returns_not_found_for_an_unknown_hostname()
    {
        var response = await _client.PostAsync("/api/agents/does-not-exist/reissue-certificate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private record VersionResponse(string server, string protocol, string database);
}
