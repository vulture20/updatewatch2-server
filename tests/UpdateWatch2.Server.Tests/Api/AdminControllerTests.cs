using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Api;

public class AdminControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;

    public AdminControllerTests(WebApplicationFactory<Program> factory)
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
    public async Task Get_returns_the_seeded_defaults()
    {
        var settings = await _client.GetFromJsonAsync<AdminSettingsDto>("/api/admin/settings");

        Assert.NotNull(settings);
        Assert.Equal(6, settings.BruteForceMaxAttempts);
        Assert.False(settings.SmtpConfigured);
        Assert.False(settings.SmtpPasswordSet);
    }

    [Fact]
    public async Task Put_persists_changes_and_get_reflects_them()
    {
        var response = await _client.PutAsJsonAsync("/api/admin/settings", ValidUpdateRequest() with { BruteForceMaxAttempts = 9 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var settings = await _client.GetFromJsonAsync<AdminSettingsDto>("/api/admin/settings");
        Assert.NotNull(settings);
        Assert.Equal(9, settings.BruteForceMaxAttempts);
    }

    [Fact]
    public async Task Put_accepts_a_lowercase_log_level_and_normalizes_it()
    {
        var response = await _client.PutAsJsonAsync("/api/admin/settings", ValidUpdateRequest() with { LogLevel = "debug" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var settings = await _client.GetFromJsonAsync<AdminSettingsDto>("/api/admin/settings");
        Assert.Equal("DEBUG", settings!.LogLevel);
    }

    [Fact]
    public async Task Put_rejects_an_invalid_log_level()
    {
        var response = await _client.PutAsJsonAsync("/api/admin/settings", ValidUpdateRequest() with { LogLevel = "VERBOSE" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_rejects_a_zero_brute_force_max_attempts()
    {
        var response = await _client.PutAsJsonAsync("/api/admin/settings", ValidUpdateRequest() with { BruteForceMaxAttempts = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_rejects_an_out_of_range_smtp_port()
    {
        var response = await _client.PutAsJsonAsync("/api/admin/settings", ValidUpdateRequest() with { SmtpPort = 70000 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_rejects_an_unknown_smtp_encryption_value()
    {
        var response = await _client.PutAsJsonAsync("/api/admin/settings", ValidUpdateRequest() with { SmtpEncryption = "PGP" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_requires_an_admin_session()
    {
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.PutAsJsonAsync("/api/admin/settings", ValidUpdateRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_lowered_brute_force_threshold_takes_effect_immediately_for_new_logins()
    {
        // End-to-end: PUT changes the *actual* lockout behavior of
        // /api/auth/login, not just the value GET reports back.
        var putResponse = await _client.PutAsJsonAsync(
            "/api/admin/settings", ValidUpdateRequest() with { BruteForceMaxAttempts = 2 });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        using var freshClient = _factory.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            var failed = await freshClient.PostAsJsonAsync(
                "/api/auth/login", new LoginRequest(AuthTestHelper.Username, "wrong-password"));
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var lockedOut = await freshClient.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AuthTestHelper.Username, AuthTestHelper.Password));

        Assert.Equal(HttpStatusCode.Locked, lockedOut.StatusCode);
    }

    private static UpdateAdminSettingsRequest ValidUpdateRequest() => new(
        LogLevel: "INFO",
        BruteForceMaxAttempts: 6,
        BruteForceWindowMinutes: 5,
        BruteForceLockoutMinutes: 30,
        SmtpHost: "smtp.example.com",
        SmtpPort: 587,
        SmtpUsername: null,
        SmtpPassword: null,
        SmtpEncryption: "StartTls",
        SmtpFromAddress: "updatewatch2@example.com",
        SmtpFromName: "UpdateWatch2",
        NotificationUpdatesPerMachineThreshold: 5,
        NotificationAffectedMachinesThreshold: 10);
}
