using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Api;

/// <summary>
/// Confirms <c>Program.cs</c> actually wires <c>UPDATEWATCH2_TRUSTED_PROXY</c>
/// into the real <c>ForwardedHeadersOptions</c> pipeline —
/// <c>TrustedProxyOptionsConfiguratorTests</c> already covers the parsing
/// logic in isolation; this proves the env var genuinely reaches it and
/// changes real request handling.
///
/// A single, dedicated <see cref="WebApplicationFactory{TEntryPoint}"/>,
/// not <c>AuthControllerTests</c>' shared <c>IClassFixture</c> —
/// <c>AddOptions&lt;ForwardedHeadersOptions&gt;().Configure&lt;ILoggerFactory&gt;(...)</c>
/// resolves once per host build and is cached in the singleton
/// <c>IOptions&lt;T&gt;</c>, unlike <c>UPDATEWATCH2_TRUSTEDIP</c>'s own live
/// per-request env read (which is why <c>AuthControllerTests</c> can toggle
/// that one per-<c>[Fact]</c> against one shared fixture but this one
/// can't) — the same class of problem <c>AdminAccountServiceTests</c>
/// already hit for <c>UPDATEWATCH2_RESET_ADMIN_PASSWORD</c>. Both cases
/// below use the identical configured value, so a single factory build
/// covers both — deliberately not one-factory-per-case: this project has
/// no existing precedent for more than one ad-hoc
/// <c>WebApplicationFactory&lt;Program&gt;</c> instance in the same test
/// run (every other user is a single shared <c>IClassFixture</c> per
/// class), and <c>Program.cs</c>'s own <c>ConfigureKestrel</c>/<c>ListenAnyIP</c>
/// calls run unconditionally even under test — building two in quick
/// succession produced a real, if intermittent, cross-test failure cascade
/// in a full-suite run that a single build doesn't risk.
///
/// <see cref="WebApplicationFactory{TEntryPoint}"/>'s in-memory
/// <c>TestServer</c> never sets <c>HttpContext.Connection.RemoteIpAddress</c>
/// (confirmed by hand) — and ASP.NET Core's forwarded-headers middleware
/// skips its allow-list check entirely whenever <c>RemoteIpAddress</c> is
/// null, applying forwarded headers unconditionally regardless of
/// <c>KnownProxies</c>/<c>KnownIPNetworks</c>. So a plain <c>HttpClient</c>
/// request (as every other test in this project uses) can never actually
/// exercise the allow-list either way — both cases below use
/// <c>TestServer.SendAsync(Action&lt;HttpContext&gt;)</c> instead, to set a
/// concrete <c>RemoteIpAddress</c> before the pipeline runs.
/// </summary>
public class TrustedProxyEnvironmentVariableTests : IAsyncLifetime
{
    private const string TrustedProxy = "10.0.0.5";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-trusted-proxy-test-{Guid.NewGuid()}.sqlite");
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("UPDATEWATCH2_TRUSTED_PROXY", TrustedProxy);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.WithoutBackgroundWorkers().ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Path"] = _dbPath })));
        await AuthTestHelper.SeedAdminAsync(_factory.Services);
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        Environment.SetEnvironmentVariable("UPDATEWATCH2_TRUSTED_PROXY", null);
        File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_untrusted_peers_X_Forwarded_Proto_is_ignored_while_a_trusted_peers_is_honored()
    {
        var untrustedSetCookie = await SendLoginAsync(IPAddress.Parse("203.0.113.9"));
        var trustedSetCookie = await SendLoginAsync(IPAddress.Parse(TrustedProxy));

        // Untrusted peer -> X-Forwarded-Proto ignored -> the test host's
        // own plain-HTTP scheme wins -> cookie NOT Secure.
        Assert.DoesNotContain("secure", untrustedSetCookie, StringComparison.OrdinalIgnoreCase);
        // Trusted peer -> X-Forwarded-Proto honored -> cookie IS Secure,
        // proving this isn't just "the feature always rejects."
        Assert.Contains("secure", trustedSetCookie, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> SendLoginAsync(IPAddress remoteIp)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new LoginRequest(AuthTestHelper.Username, AuthTestHelper.Password));

        var context = await _factory!.Server.SendAsync(ctx =>
        {
            ctx.Connection.RemoteIpAddress = remoteIp;
            ctx.Request.Method = "POST";
            ctx.Request.Path = "/api/auth/login";
            ctx.Request.Headers["X-Forwarded-Proto"] = "https";
            ctx.Request.ContentType = "application/json";
            ctx.Request.Body = new MemoryStream(body);
            ctx.Request.ContentLength = body.Length;
        });

        return Assert.Single(context.Response.Headers["Set-Cookie"])!;
    }
}
