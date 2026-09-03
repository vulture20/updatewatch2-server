using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Tests.TestHelpers;
using UpdateWatch2.Server.Updates;

namespace UpdateWatch2.Server.Tests.Api;

public class UpdatesEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;

    public UpdatesEndpointTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task Reporting_updates_for_unknown_agent_returns_not_found()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/agents/does-not-exist/updates",
            new ReportUpdatesRequest([], RebootRequired: false));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reported_updates_show_up_in_agent_detail_and_updates_list()
    {
        await SeedAgentAsync("test-host");

        var report = new ReportUpdatesRequest(
            [new ReportedUpdate("Security Update", "KB123456", "A security fix")],
            RebootRequired: true);

        var reportResponse = await _client.PostAsJsonAsync("/api/agents/test-host/updates", report);
        Assert.Equal(HttpStatusCode.NoContent, reportResponse.StatusCode);

        var updates = await _client.GetFromJsonAsync<List<UpdateItemDto>>("/api/agents/test-host/updates");
        Assert.NotNull(updates);
        var single = Assert.Single(updates);
        Assert.Equal("Security Update", single.Title);
        Assert.Equal("KB123456", single.PackageId);

        var detail = await _client.GetFromJsonAsync<AgentDetail>("/api/agents/test-host");
        Assert.NotNull(detail);
        Assert.True(detail.RebootRequired);
        Assert.Equal(1, detail.PendingUpdateCount);
    }

    [Fact]
    public async Task Trigger_install_for_unknown_agent_returns_not_found()
    {
        var response = await _client.PostAsync("/api/agents/does-not-exist/install", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Trigger_install_for_known_agent_accepts()
    {
        await SeedAgentAsync("install-host");

        var response = await _client.PostAsync("/api/agents/install-host/install", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task SeedAgentAsync(string hostname)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Agents.Add(new Agent { Hostname = hostname, Approved = true });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    private record AgentDetail(string Hostname, string? DnsName, string? OperatingSystem, string? IpAddress,
        string? AgentVersion, bool Approved, bool RebootRequired, int PendingUpdateCount, DateTimeOffset? LastAliveAt);
}
