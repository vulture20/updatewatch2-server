using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Api;

public class AuditLogControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;
    private HttpClient _anonymousClient = null!;

    public AuditLogControllerTests(WebApplicationFactory<Program> factory)
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
        var response = await _anonymousClient.GetAsync("/api/admin/audit-log");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_defaults_to_page_1_and_pageSize_50()
    {
        var response = await _client.GetAsync("/api/admin/audit-log");

        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<AuditLogPageDto>();
        Assert.NotNull(page);
        Assert.Equal(1, page!.Page);
        Assert.Equal(50, page.PageSize);
    }

    [Fact]
    public async Task Get_reflects_entries_written_through_the_service()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
            await auditLog.LogAsync("admin", "agent.approve", "host-1");
        }

        var response = await _client.GetAsync("/api/admin/audit-log");

        // Not asserting an exact TotalCount — AuthTestHelper.LoginAsync's
        // own login already wrote its own audit entry, so this list
        // legitimately has more than just the one this test added.
        var page = await response.Content.ReadFromJsonAsync<AuditLogPageDto>();
        Assert.Contains(page!.Entries, e => e.Action == "agent.approve" && e.Details == "host-1");
    }

    [Fact]
    public async Task Get_applies_the_search_query_parameter()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
            await auditLog.LogAsync("admin", "agent.approve", "host-1");
            await auditLog.LogAsync("admin", "agent.delete", "host-2");
        }

        var response = await _client.GetAsync("/api/admin/audit-log?search=delete");

        var page = await response.Content.ReadFromJsonAsync<AuditLogPageDto>();
        Assert.Equal(1, page!.TotalCount);
        Assert.Equal("agent.delete", page.Entries[0].Action);
    }

    [Fact]
    public async Task Get_with_unlimited_true_returns_every_entry_unpaged()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
            for (var i = 0; i < 5; i++)
            {
                await auditLog.LogAsync("admin", $"action.{i}");
            }
        }

        var response = await _client.GetAsync("/api/admin/audit-log?unlimited=true");

        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<AuditLogPageDto>();
        Assert.NotNull(page);
        Assert.Equal(1, page!.Page);
        // At least the 5 entries this test added, plus AuthTestHelper.LoginAsync's own —
        // the point is the explicit flag genuinely bypassed pagination.
        Assert.True(page.Entries.Count >= 5);
        Assert.Equal(page.Entries.Count, page.PageSize);
    }

    [Fact]
    public async Task Get_ignores_a_negative_pageSize_when_unlimited_is_not_set()
    {
        // "Unlimited" is its own explicit query parameter now — a negative
        // pageSize alone (even -1, this endpoint's old, since-removed
        // sentinel value) is no longer a second way to request it; it's
        // just out-of-range input, clamped to the ordinary [1, 200] minimum
        // like any other pageSize.
        var response = await _client.GetAsync("/api/admin/audit-log?pageSize=-1");

        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<AuditLogPageDto>();
        Assert.NotNull(page);
        Assert.Equal(1, page!.PageSize);
    }
}
