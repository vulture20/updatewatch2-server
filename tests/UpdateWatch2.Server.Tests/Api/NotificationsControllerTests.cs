using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Notifications;
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

    /// <summary>
    /// Found by a real user report: a successful send returned `Ok()` — an
    /// HTTP 200 with an EMPTY body — but the web client's own
    /// `apiClient.post` only skips `response.json()` on 204; a 200 with no
    /// body threw a JSON parse error client-side and showed a generic
    /// error banner, even though the actual email had already been sent
    /// and delivered by the time that happened. A real `SmtpClient` send
    /// can't run in CI (no mail server) — the regression this guards is
    /// about the HTTP response's *shape* on success, not SMTP delivery
    /// itself, so a fake `IEmailNotificationService` that just completes
    /// stands in for a real send.
    /// </summary>
    [Fact]
    public async Task Test_email_returns_204_No_Content_on_success_not_200_with_an_unparseable_empty_body()
    {
        using var factoryWithFakeEmail = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailNotificationService>();
                services.AddScoped<IEmailNotificationService, FakeEmailNotificationService>();
            }));
        using var client = factoryWithFakeEmail.CreateClient();
        await AuthTestHelper.LoginAsync(client);

        // SMTP must read as "configured" for the controller to even
        // attempt a send — same shape AdminControllerTests' own
        // ValidUpdateRequest uses, trimmed to just what this needs.
        var settingsResponse = await client.PutAsJsonAsync("/api/admin/settings", new UpdateAdminSettingsRequest(
            LogLevel: "INFO", BruteForceMaxAttempts: 6, BruteForceWindowMinutes: 5, BruteForceLockoutMinutes: 30,
            SmtpHost: "smtp.example.com", SmtpPort: 587, SmtpUsername: null, SmtpPassword: null, SmtpEncryption: "StartTls",
            SmtpFromAddress: "updatewatch2@example.com", SmtpFromName: "UpdateWatch2", NotificationRecipientAddress: null,
            NotificationUpdatesPerMachineThreshold: 5, NotificationAffectedMachinesThreshold: 10,
            AdEnabled: false, AdHost: "", AdPort: 389, AdEncryption: "StartTls", AdBindDn: "", AdBindPassword: null,
            AdBaseDn: "", AdUserSearchFilter: "(&(objectClass=user)(sAMAccountName={0}))", AdLoginGroupDn: "",
            AgentCertificateValidityDays: 730));
        settingsResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/admin/notifications/test-email", new { toAddress = "admin@example.com" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }

    private record ErrorsDto(string[] Errors);

    private class FakeEmailNotificationService : IEmailNotificationService
    {
        public Task SendTestEmailAsync(string toAddress, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsHealthyAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task SendNotificationAsync(string toAddress, string subject, string body, CancellationToken ct = default) => Task.CompletedTask;
    }
}
