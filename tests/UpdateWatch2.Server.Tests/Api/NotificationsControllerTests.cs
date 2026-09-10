using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Api;

/// <summary>
/// Covers the auth gate and both guard clauses (invalid address, SMTP not
/// configured) — deliberately not an actual successful send, the same
/// "no automated test can drive a real SMTP server" reasoning
/// <c>EmailNotificationService</c> itself has no test file at all for. A
/// freshly seeded admin-settings row has no SMTP host/from-address set, so
/// "not configured" is this suite's own natural default state, needing no
/// extra setup to exercise.
/// </summary>
public class NotificationsControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;
    private HttpClient _anonymousClient = null!;

    public NotificationsControllerTests(WebApplicationFactory<Program> factory)
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
    public async Task Test_email_requires_an_admin_session()
    {
        var response = await _anonymousClient.PostAsJsonAsync("/api/admin/notifications/test-email", new { toAddress = "admin@example.com" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Test_email_rejects_an_address_with_no_at_sign()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/notifications/test-email", new { toAddress = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Test_email_rejects_an_empty_address()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/notifications/test-email", new { toAddress = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Test_email_fails_with_a_clear_error_when_SMTP_is_not_configured()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/notifications/test-email", new { toAddress = "admin@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorsDto>();
        Assert.Contains(body!.Errors, e => e.Contains("SMTP", StringComparison.OrdinalIgnoreCase));
    }

    private record ErrorsDto(string[] Errors);
}
