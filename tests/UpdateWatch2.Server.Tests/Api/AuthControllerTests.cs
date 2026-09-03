using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Api;

public class AuthControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public AuthControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Path"] = _dbPath,
                    // Keep the lockout test fast and independent of the real default.
                    ["BruteForce:MaxAttempts"] = "3",
                })));
    }

    public async Task InitializeAsync() => await AuthTestHelper.SeedAdminAsync(_factory.Services);

    public Task DisposeAsync()
    {
        File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Me_reports_unauthenticated_before_login()
    {
        using var client = _factory.CreateClient();

        var me = await client.GetFromJsonAsync<MeResponseDto>("/api/auth/me");

        Assert.NotNull(me);
        Assert.False(me.authenticated);
    }

    [Fact]
    public async Task Login_over_plain_http_does_not_mark_the_cookie_secure()
    {
        // Regression test: CookieSecurePolicy.Always marked the cookie
        // Secure even over plain HTTP. A real browser silently refuses to
        // store a Secure cookie from an insecure connection — login
        // appeared to succeed (200 + body), but the browser never actually
        // kept the session, so the very next request bounced straight
        // back to the login page with no error shown. This asserts on the
        // Set-Cookie header directly (what a browser actually enforces)
        // rather than on whether *this* .NET test client's own cookie jar
        // still sends the cookie afterward — that's not a reliable proxy:
        // it kept resending the Secure-flagged cookie over plain HTTP even
        // under the old, broken policy (confirmed by hand), so a
        // client-behavior-only assertion would not have caught this bug.
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") });

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AuthTestHelper.Username, AuthTestHelper.Password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var setCookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie"));
        Assert.DoesNotContain("secure", setCookie, StringComparison.OrdinalIgnoreCase);

        // The feature should still work normally over plain HTTP, of course.
        var me = await client.GetFromJsonAsync<MeResponseDto>("/api/auth/me");
        Assert.True(me?.authenticated);
    }

    [Fact]
    public async Task Login_over_https_marks_the_cookie_secure()
    {
        // WebApplicationFactory.CreateClient()'s default base address is
        // http://localhost, not https (confirmed by hand — don't assume
        // otherwise) — request https:// explicitly.
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AuthTestHelper.Username, AuthTestHelper.Password));

        var setCookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_with_correct_credentials_succeeds_and_establishes_a_session()
    {
        using var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AuthTestHelper.Username, AuthTestHelper.Password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var me = await client.GetFromJsonAsync<MeResponseDto>("/api/auth/me");
        Assert.NotNull(me);
        Assert.True(me.authenticated);
        Assert.Equal(AuthTestHelper.Username, me.username);
    }

    [Fact]
    public async Task Login_with_wrong_password_fails_without_establishing_a_session()
    {
        using var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AuthTestHelper.Username, "wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);

        var me = await client.GetFromJsonAsync<MeResponseDto>("/api/auth/me");
        Assert.NotNull(me);
        Assert.False(me.authenticated);
    }

    [Fact]
    public async Task Repeated_failed_logins_lock_the_account_out()
    {
        using var client = _factory.CreateClient();

        // BruteForce:MaxAttempts is overridden to 3 above.
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login", new LoginRequest(AuthTestHelper.Username, "wrong-password"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var lockedOutResponse = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AuthTestHelper.Username, AuthTestHelper.Password));

        Assert.Equal(HttpStatusCode.Locked, lockedOutResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        using var client = _factory.CreateClient();
        await AuthTestHelper.LoginAsync(client);

        var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var me = await client.GetFromJsonAsync<MeResponseDto>("/api/auth/me");
        Assert.NotNull(me);
        Assert.False(me.authenticated);
    }

    [Fact]
    public async Task Logout_requires_an_existing_session()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Change_password_with_correct_current_password_succeeds_and_new_password_works_next_login()
    {
        using var client = _factory.CreateClient();
        await AuthTestHelper.LoginAsync(client);
        const string newPassword = "An0ther$ecureTestPassw0rd!";

        var changeResponse = await client.PutAsJsonAsync(
            "/api/auth/password", new ChangePasswordRequest(AuthTestHelper.Password, newPassword));
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        using var freshClient = _factory.CreateClient();
        var reloginResponse = await freshClient.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AuthTestHelper.Username, newPassword));
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);
    }

    [Fact]
    public async Task Change_password_rejects_a_wrong_current_password()
    {
        using var client = _factory.CreateClient();
        await AuthTestHelper.LoginAsync(client);

        var response = await client.PutAsJsonAsync(
            "/api/auth/password", new ChangePasswordRequest("wrong-current-password", "An0ther$ecureTestPassw0rd!"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Change_password_rejects_a_new_password_that_fails_the_complexity_policy()
    {
        using var client = _factory.CreateClient();
        await AuthTestHelper.LoginAsync(client);

        var response = await client.PutAsJsonAsync(
            "/api/auth/password", new ChangePasswordRequest(AuthTestHelper.Password, "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Deliberately lowercase to match the wire format (System.Text.Json's
    // default web naming policy camelCases property names on the way out;
    // GetFromJsonAsync doesn't case-insensitively match back onto the
    // PascalCase production DTO without extra options), rather than
    // depending on that behavior.
    private record MeResponseDto(bool authenticated, string? username);
}
