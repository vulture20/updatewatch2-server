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
        Assert.Equal("0.5.1", response.server);
        Assert.Equal("0.1.0", response.protocol);
        Assert.Equal("0.4.0", response.database);
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

    private record VersionResponse(string server, string protocol, string database);
}
