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
            builder.WithoutBackgroundWorkers().ConfigureAppConfiguration((_, config) =>
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
    public async Task Login_falls_through_to_AD_for_an_unknown_local_username_and_fails_cleanly_when_AD_is_disabled()
    {
        // Exercises the AuthController -> IActiveDirectoryAuthService wiring
        // without needing a real directory: AdOptions.Enabled defaults to
        // false, so ActiveDirectoryAuthService short-circuits before ever
        // opening an LDAP connection. The actual bind/search/group-membership
        // logic against a real LDAP server was verified by hand — see
        // CLAUDE.md's note on ActiveDirectoryAuthService for how to repeat
        // that.
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("someone-not-the-local-admin", "whatever"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
    public async Task A_spoofed_X_Forwarded_For_header_does_not_bypass_the_lockout_even_when_UPDATEWATCH2_TRUSTEDIP_is_configured()
    {
        // Security review regression test: ForwardedHeadersOptions clears
        // KnownProxies/KnownIPNetworks (deliberately, for X-Forwarded-Proto —
        // see Program.cs's own comment), which used to also make
        // X-Forwarded-For fully attacker-controllable, letting anyone claim
        // to be inside the UPDATEWATCH2_TRUSTEDIP range and bypass the
        // brute-force lockout entirely with one extra header. The fix
        // (RealRemoteIpAccessor) captures the real TCP peer before
        // UseForwardedHeaders() can touch it, and AuthController.Login now
        // keys the lockout decision off that instead.
        Environment.SetEnvironmentVariable("UPDATEWATCH2_TRUSTEDIP", "10.0.0.0/8");
        try
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.1.2.3");

            // BruteForce:MaxAttempts is overridden to 3 above.
            for (var i = 0; i < 3; i++)
            {
                await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(AuthTestHelper.Username, "wrong-password"));
            }

            var lockedOutResponse = await client.PostAsJsonAsync(
                "/api/auth/login", new LoginRequest(AuthTestHelper.Username, AuthTestHelper.Password));

            Assert.Equal(HttpStatusCode.Locked, lockedOutResponse.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UPDATEWATCH2_TRUSTEDIP", null);
        }
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
    public async Task Logout_invalidates_the_ticket_server_side_so_a_copy_of_the_cookie_stops_working_elsewhere()
    {
        // Security review finding: logout used to only ever clear the
        // CALLING browser's own cookie — a copy of the same raw cookie
        // value used elsewhere (the "stolen cookie" scenario) kept
        // authenticating indefinitely. This proves the fix actually
        // revokes it server-side, not just client-side.
        using var loginClient = _factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AuthTestHelper.Username, AuthTestHelper.Password));
        var rawCookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie")).Split(';')[0];

        // A completely independent client, presenting a copy of the exact
        // same raw cookie value rather than sharing loginClient's own
        // cookie jar — simulating a stolen/copied cookie used from
        // somewhere else entirely.
        using var copiedCookieClient = _factory.CreateClient();
        copiedCookieClient.DefaultRequestHeaders.Add("Cookie", rawCookie);

        var meBeforeLogout = await copiedCookieClient.GetFromJsonAsync<MeResponseDto>("/api/auth/me");
        Assert.True(meBeforeLogout?.authenticated); // sanity check: the copied cookie really does work before logout

        var logoutResponse = await loginClient.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var meAfterLogout = await copiedCookieClient.GetFromJsonAsync<MeResponseDto>("/api/auth/me");
        Assert.False(meAfterLogout?.authenticated);
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
    public async Task Change_password_invalidates_the_ticket_server_side_so_a_copy_of_the_cookie_stops_working_elsewhere()
    {
        // Same finding as the logout regression test above, but for a
        // password change: it used to leave every already-issued cookie
        // (including a stolen copy) working unaffected — exactly the
        // scenario a "change my password" action is often taken to
        // respond to.
        using var loginClient = _factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(AuthTestHelper.Username, AuthTestHelper.Password));
        var rawCookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie")).Split(';')[0];

        using var copiedCookieClient = _factory.CreateClient();
        copiedCookieClient.DefaultRequestHeaders.Add("Cookie", rawCookie);
        var meBeforeChange = await copiedCookieClient.GetFromJsonAsync<MeResponseDto>("/api/auth/me");
        Assert.True(meBeforeChange?.authenticated);

        var changeResponse = await loginClient.PutAsJsonAsync(
            "/api/auth/password", new ChangePasswordRequest(AuthTestHelper.Password, "An0ther$ecureTestPassw0rd!"));
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        var meAfterChange = await copiedCookieClient.GetFromJsonAsync<MeResponseDto>("/api/auth/me");
        Assert.False(meAfterChange?.authenticated);
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
