using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace UpdateWatch2.Server.Tests.Api;

public class ApiEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly HttpClient _client;

    public ApiEndpointTests(WebApplicationFactory<Program> factory)
    {
        var configured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Path"] = _dbPath,
                })));

        _client = configured.CreateClient();
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
        Assert.Equal("0.1.0", response.server);
        Assert.Equal("0.1.0", response.protocol);
        Assert.Equal("0.1.0", response.database);
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

    public void Dispose()
    {
        _client.Dispose();
        File.Delete(_dbPath);
    }

    private record VersionResponse(string server, string protocol, string database);
}
