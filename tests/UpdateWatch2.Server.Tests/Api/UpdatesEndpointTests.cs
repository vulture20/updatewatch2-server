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

    // ReportUpdates now requires the agent's own mutual-TLS client
    // certificate (updatewatch2-server#1) rather than being anonymous —
    // WebApplicationFactory's in-memory TestServer bypasses Kestrel/real TLS
    // entirely, so it can't present a client certificate and the success
    // path can't be exercised through this test harness. That path is
    // covered by CertificateValidatorTests/AgentRegistrationServiceTests
    // (the logic) and by a live curl/real-agent walkthrough (the actual
    // mTLS handshake) — see this feature's commit history. What's covered
    // here is that the route genuinely rejects non-cert callers, including
    // an authenticated *admin* (cookie-session) caller, which must not be
    // treated as equivalent to a cert-authenticated agent.
    [Fact]
    public async Task Reporting_updates_without_a_client_certificate_is_rejected_even_for_a_logged_in_admin()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/agents/does-not-exist/updates",
            new ReportUpdatesRequest([], RebootRequired: false));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reporting_updates_without_a_client_certificate_is_rejected_when_fully_anonymous()
    {
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.PostAsJsonAsync(
            "/api/agents/does-not-exist/updates",
            new ReportUpdatesRequest([], RebootRequired: false));

        // Confirmed by hand, not assumed: the certificate-auth handler has
        // no browser-style "challenge" redirect, so a request with no
        // client certificate at all lands on 403 here too, the same as an
        // authenticated-but-wrong-scheme (admin cookie) caller above — not
        // the 401 a missing-cookie request against a cookie-gated route
        // would get. Both are "rejected", which is what actually matters.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Previously_reported_updates_show_up_in_agent_detail_and_updates_list()
    {
        // Seeds directly via the DbContext rather than through the
        // (now cert-gated) POST endpoint — see the comment above. This
        // still exercises the two admin-facing GET routes end to end.
        await SeedAgentWithUpdateAsync("test-host", "Security Update", "KB123456", rebootRequired: true);

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

    [Fact]
    public async Task Trigger_install_surfaces_as_a_pending_install_request_in_agent_detail()
    {
        await SeedAgentAsync("install-detail-host");

        await _client.PostAsync("/api/agents/install-detail-host/install", content: null);

        var detail = await _client.GetFromJsonAsync<AgentDetail>("/api/agents/install-detail-host");
        Assert.NotNull(detail);
        Assert.NotNull(detail.PendingInstallRequestedAt);
    }

    // install-ack is agent-facing (mTLS), the same as ReportUpdates above —
    // WebApplicationFactory can't present a client certificate, so only the
    // rejection path is exercised here; the success path is covered by
    // UpdateServiceTests (the logic) plus a live run (the actual handshake).
    [Fact]
    public async Task Acknowledging_install_without_a_client_certificate_is_rejected_even_for_a_logged_in_admin()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/agents/does-not-exist/install-ack",
            new InstallAckRequest(InstallOutcome.Succeeded));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task SeedAgentAsync(string hostname)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Agents.Add(new Agent { Hostname = hostname, Approved = true });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds an agent with one pending update directly via the DbContext,
    /// mirroring exactly what UpdateService.ReportUpdatesAsync itself
    /// writes (UpdateItem row + Agent.PendingUpdateCount/RebootRequired) —
    /// see the comment on the tests that use this for why the POST endpoint
    /// itself can no longer be driven from this test harness.
    /// </summary>
    private async Task SeedAgentWithUpdateAsync(string hostname, string title, string? packageId, bool rebootRequired)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = new Agent { Hostname = hostname, Approved = true, PendingUpdateCount = 1, RebootRequired = rebootRequired };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        db.UpdateItems.Add(new UpdateItem { AgentId = agent.Id, Title = title, PackageId = packageId });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    private record AgentDetail(string Hostname, string? DnsName, string? OperatingSystem, string? IpAddress,
        string? AgentVersion, bool Approved, bool RebootRequired, int PendingUpdateCount, DateTimeOffset? LastAliveAt,
        string? ClientCertificateThumbprint, DateTimeOffset? ClientCertificateIssuedAt, DateTimeOffset? ClientCertificateExpiresAt,
        DateTimeOffset? PendingInstallRequestedAt, string? LastInstallOutcome, DateTimeOffset? LastInstallCompletedAt);
}
