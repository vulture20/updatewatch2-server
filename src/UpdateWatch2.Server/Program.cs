using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Demo;
using UpdateWatch2.Server.Notifications;
using UpdateWatch2.Server.Updates;

var builder = WebApplication.CreateBuilder(args);

// UPDATEWATCH2_LOGLEVEL overrides the configured default log level, per
// CLAUDE.md ("Server: über die Oberfläche oder über die Umgebungsvariable
// UPDATEWATCH2_LOGLEVEL"). If it's not set, fall back to whatever an admin
// last saved via the Administration UI (Admin/AdminSettingsStore) — read
// with a raw, read-only connection since this runs before the DI container
// (and AppDbContext) exists. This deliberately does NOT use the same
// lazy-resolution path the AddDbContext factory below uses: it's a
// best-effort probe of whatever appsettings.json's Database:Path says
// *before* WebApplicationFactory-in-tests applies its config override
// (see that factory's comment), so in tests it harmlessly looks at the
// wrong/nonexistent file and finds nothing — never creates a directory or
// touches a file, unlike the real DbContext path. Either way, a persisted
// value here only takes effect on the next process start: there's no
// hot-reload of the running logger's minimum level yet (see
// IAdminSettingsStore.LogLevel's doc comment).
//
// This sets builder.Configuration["Logging:LogLevel:Default"] directly
// rather than calling builder.Logging.SetMinimumLevel(...) — verified by
// hand that SetMinimumLevel is silently a no-op here: ASP.NET Core's
// default host logging re-reads "Logging:LogLevel:*" from IConfiguration
// reactively, and that config-sourced rule wins over the imperative
// MinLevel floor SetMinimumLevel sets, regardless of call order. Writing
// the value ASP.NET Core's own config-driven filter reads is the only
// version of this that was confirmed to actually change verbosity.
var logLevelEnv = Environment.GetEnvironmentVariable("UPDATEWATCH2_LOGLEVEL");
var effectiveLogLevel = logLevelEnv ?? TryReadPersistedLogLevel(
    Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Database:Path"] ?? "data/updatewatch2.sqlite")));
if (effectiveLogLevel is not null && Enum.TryParse<LogLevel>(MapLogLevel(effectiveLogLevel), out _))
{
    builder.Configuration["Logging:LogLevel:Default"] = MapLogLevel(effectiveLogLevel);
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Resolved lazily (per DbContext construction, via DI) rather than once
// eagerly here — WebApplicationFactory-based tests apply their
// configuration overrides only once the host finishes building, which is
// after this file's top-level code has already run.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var dbPath = config["Database:Path"] ?? "data/updatewatch2.sqlite";
    var fullDbPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, dbPath));
    Directory.CreateDirectory(Path.GetDirectoryName(fullDbPath)!);
    options.UseSqlite($"Data Source={fullDbPath}");
});

// Persists Data Protection keys (used to encrypt/sign the auth cookie)
// next to the SQLite database rather than the container's ephemeral
// default (~/.aspnet/DataProtection-Keys) — otherwise every container
// restart invalidates every admin session. Rides along with the same
// volume Database:Path is already on (see docker/Dockerfile's /app/data
// VOLUME), no separate config knob needed. Read eagerly here rather than
// lazily like AddDbContext above — unlike the SQLite path, tests sharing
// this default directory across WebApplicationFactory instances isn't a
// correctness issue (Data Protection is explicitly designed for multiple
// instances to share one key ring).
var keysDirectory = new DirectoryInfo(Path.Combine(
    Path.GetDirectoryName(Path.GetFullPath(Path.Combine(
        builder.Environment.ContentRootPath, builder.Configuration["Database:Path"] ?? "data/updatewatch2.sqlite")))!,
    "keys"));
builder.Services.AddDataProtection()
    .SetApplicationName("UpdateWatch2.Server")
    .PersistKeysToFileSystem(keysDirectory);

// These five are bound purely as the compiled-in defaults AdminSettingsStore
// seeds its DB row from on first run — the database is authoritative after
// that, not these IOptions<T> snapshots. See each options class's doc comment.
builder.Services.Configure<BruteForceOptions>(builder.Configuration.GetSection(BruteForceOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<NotificationThresholdOptions>(builder.Configuration.GetSection(NotificationThresholdOptions.SectionName));
builder.Services.Configure<AdOptions>(builder.Configuration.GetSection(AdOptions.SectionName));
builder.Services.Configure<CertificateOptions>(builder.Configuration.GetSection(CertificateOptions.SectionName));

builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<IUpdateService, UpdateService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddSingleton<ITrustedIpRangeProvider, EnvironmentTrustedIpRangeProvider>();
builder.Services.AddSingleton<IBruteForceLoginService, BruteForceLoginService>();
builder.Services.AddScoped<IEmailNotificationService, EmailNotificationService>();
builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
builder.Services.AddScoped<IActiveDirectoryAuthService, ActiveDirectoryAuthService>();
builder.Services.AddSingleton<IAdminSettingsStore, AdminSettingsStore>();
builder.Services.AddScoped<IDemoDataSeeder, DemoDataSeeder>();

// The frontend (server/web) is a separate origin in development (its own
// Vite dev server port) and, even in a same-origin production deployment
// behind one reverse proxy, this keeps the API usable from other origins
// an admin explicitly configures. Credentialed requests need an explicit
// origin list, not "*" — AllowCredentials + AllowAnyOrigin is rejected by
// browsers anyway.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "https://localhost:5173"];
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// The browser/admin-UI port (8080, below) almost never terminates TLS
// itself (see docker/Dockerfile — a reverse proxy in front is expected to
// terminate HTTPS, per CLAUDE.md/.env.example). Trusting X-Forwarded-Proto
// from any proxy (not just loopback, the default) is what lets
// CookieSecurePolicy.SameAsRequest below correctly mark the auth cookie
// Secure when the *original* client connection was HTTPS, even though
// Kestrel itself only ever sees HTTP on that port. The real security
// boundary here is network-level (only the reverse proxy can reach this
// container), not this middleware's proxy allowlist. This has no bearing
// on the agent-facing 8443 port below, which Kestrel terminates directly.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Certificate-based mutual TLS is the security backbone for agent-server
// communication (CLAUDE.md) — see UpdateWatch2.Server.Certificates for the
// design rationale (internal CA, why the server leaf auto-regenerates,
// etc.). This has to happen before builder.Build() because Kestrel's
// listener configuration (right below) needs the server's own TLS leaf
// certificate already in hand, and Kestrel is configured as part of the
// WebApplicationBuilder, not after the app exists. It's pure filesystem
// I/O with no DB dependency, unlike the admin-account/settings seeding
// later in this file, which does need a built app's DI container/DbContext
// and so runs in a scope after builder.Build() instead.
var certsPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Certs:Path"] ?? "certs"));
var certificateAuthority = new InternalCertificateAuthority(certsPath);
var serverHostname = Environment.GetEnvironmentVariable("UPDATEWATCH2_SERVER_HOSTNAME") ?? Environment.MachineName;
certificateAuthority.EnsureServerLeaf(serverHostname);
builder.Services.AddSingleton<ICertificateAuthority>(certificateAuthority);
builder.Services.AddScoped<ICertificateValidator, CertificateValidator>();
builder.Services.AddScoped<IAgentRegistrationService, AgentRegistrationService>();

// Two listeners, not one: 8080 stays plain HTTP for the browser-facing
// admin UI/API, unchanged, still reverse-proxy-terminated (see above). 8443
// is new — Kestrel-direct-TLS-terminated, no proxy in front, dedicated to
// agent traffic (matches AgentOptions.ServerPort's existing default on the
// agent side, not a coincidence). ClientCertificateMode.AllowCertificate
// (not Require) because agent registration must be reachable with no
// client certificate at all — the agent doesn't have one yet at first
// contact; every other agent route enforces a certificate via the
// AgentCertificate authorization policy below instead, layered on top of
// this Kestrel-level allowance, not in place of it.
//
// Explicit Listen calls here replace ASPNETCORE_URLS entirely rather than
// merging with it (confirmed by hand) — so ASPNETCORE_URLS is no longer
// used at all; see docker/Dockerfile's comment on removing it. Ports are
// configurable (Kestrel:HttpPort / Kestrel:AgentPort, defaulting to the
// documented 8080/8443) purely so local/CI runs can avoid a port already
// taken on the host — the Docker image's EXPOSE/compose port mappings
// assume the defaults and were not designed to be reconfigured routinely.
var httpPort = builder.Configuration.GetValue("Kestrel:HttpPort", 8080);
var agentPort = builder.Configuration.GetValue("Kestrel:AgentPort", 8443);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(httpPort);
    options.ListenAnyIP(agentPort, listenOptions =>
    {
        listenOptions.UseHttps(https =>
        {
            // A selector, not a fixed ServerCertificate captured once at
            // startup: this reads certificateAuthority.CurrentServerLeaf on
            // every new TLS connection, so an admin-triggered CA rotation
            // (updatewatch2-server#6) re-issuing this leaf under a new root
            // takes effect immediately, with no service restart — the same
            // "no restart required" bar admin-mediated agent certificate
            // re-issuance already holds (updatewatch2-server#8).
            https.ServerCertificateSelector = (_, _) => certificateAuthority.CurrentServerLeaf;
            https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;

            // Confirmed by hand, not assumed: without this, Kestrel's own
            // HttpsConnectionMiddleware runs its own default client-cert
            // validation — chain-building against the OS trust store — at
            // the raw TLS layer, entirely separate from and *before* the
            // Microsoft.AspNetCore.Authentication.Certificate middleware's
            // CustomTrustStore/OnCertificateValidated logic below ever
            // runs. An agent leaf signed by our internal CA is never in the
            // OS store, so Kestrel silently aborted the TLS handshake
            // itself for every agent request (logged only at Debug:
            // "Failed to authenticate HTTPS connection... The remote
            // certificate was rejected by the provided
            // RemoteCertificateValidationCallback" — surfaced to callers as
            // a bare connection reset/"unexpected eof", not any HTTP status
            // code, which is what made this look like a protocol-level bug
            // rather than a trust-store mismatch). Accepting unconditionally
            // here defers *all* trust decisions to the authentication
            // middleware's CustomTrustStore, which is where they belong.
            https.ClientCertificateValidation = (_, _, _) => true;
        });
    });
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "UpdateWatch2.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // SameAsRequest — not Always — everywhere, not just Development:
        // marking the cookie Secure when the request came in over plain
        // HTTP means the browser silently refuses to store it at all, so
        // a correct login still bounces straight back to the login page
        // with no error (confirmed live: this shipped broken for exactly
        // the docker run / plain-HTTP walkthrough in this repo's own
        // README before this fix). SameAsRequest still marks the cookie
        // Secure whenever the request genuinely was HTTPS — directly, or
        // via X-Forwarded-Proto once UseForwardedHeaders() runs, below.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // This is an API, not an MVC app with login pages — return status
        // codes instead of redirecting to a (non-existent) login route.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddCertificate(CertificateAuthenticationSetup.SchemeName, options =>
    {
        // Chain-build against our own internal CA, not the OS trust store —
        // an agent certificate is never meant to be trusted by anything
        // else, and nothing else should be trusted here.
        options.AllowedCertificateTypes = CertificateTypes.Chained;
        options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
        // The CA's own mutable collection, not a snapshot array: a CA
        // rotation (updatewatch2-server#6) adds/removes roots on this exact
        // instance, and the certificate-authentication middleware re-reads
        // Options.CustomTrustStore on every request rather than caching it,
        // so mutating it here is visible immediately with no options
        // reload or restart.
        options.CustomTrustStore = certificateAuthority.TrustedRootCertificates;
        // Revocation is meaningless for a CA with no CRL/OCSP infrastructure
        // (see InternalCertificateAuthority's remarks) — the one-shot
        // delivery + admin-approval model is this project's substitute for
        // revocation, not a gap being silently ignored.
        options.RevocationMode = X509RevocationMode.NoCheck;
        options.Events = new CertificateAuthenticationEvents
        {
            OnCertificateValidated = async context =>
            {
                var validator = context.HttpContext.RequestServices.GetRequiredService<ICertificateValidator>();
                var result = await validator.ValidateAsync(context.ClientCertificate, context.HttpContext.RequestAborted);
                if (result.Success)
                {
                    context.Principal = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Name, result.Hostname!)], context.Scheme.Name));
                    context.Success();
                }
                else
                {
                    context.Fail(result.FailureReason ?? "Certificate rejected.");
                }
            },
        };
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy(CertificateAuthenticationSetup.AgentCertificatePolicy, policy => policy
        .AddAuthenticationSchemes(CertificateAuthenticationSetup.SchemeName)
        .RequireAuthenticatedUser()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var accounts = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
    await accounts.EnsureSeededAsync();

    var settingsStore = scope.ServiceProvider.GetRequiredService<IAdminSettingsStore>();
    await settingsStore.InitializeAsync();

    // UPDATEWATCH2_DEMOMODE — deliberately env-var-only, never an
    // admin-UI setting, the same as UPDATEWATCH2_TRUSTEDIP (CLAUDE.md).
    // Seeds a handful of realistic-looking dummy agents/updates so an
    // otherwise-empty instance is demonstrable; see Demo/DemoDataSeeder
    // for what gets created and its idempotency (safe to leave this set
    // across restarts).
    if (IsDemoModeEnabled())
    {
        await scope.ServiceProvider.GetRequiredService<IDemoDataSeeder>().EnsureSeededAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Must run before anything that inspects the request scheme/remote IP
// (the cookie auth handler's SameAsRequest check, IP logging) — see the
// ForwardedHeadersOptions comment above.
app.UseForwardedHeaders();

// Deliberately no app.UseHttpsRedirection() here: this app now has two
// listeners with very different purposes (see ConfigureKestrel above), and
// this middleware has no way to know it should redirect only the
// browser/admin-UI port (8080) toward its external reverse-proxy HTTPS URL
// and never touch the agent-only 8443 port. 8080 was always meant to stay
// plain HTTP directly (TLS is the reverse proxy's job, per
// CookieSecurePolicy.SameAsRequest above) — this middleware was never
// doing real work for that path even before 8443 existed.

// Serves the built web/ SPA from wwwroot when present (the Docker image
// copies it in — see docker/Dockerfile) so the API and admin UI ship as
// one deployable container, per CLAUDE.md. In local `dotnet run` dev,
// wwwroot only has the placeholder .gitkeep — these are harmless no-ops
// and the frontend is served by its own Vite dev server instead (see
// web/README.md).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Client-side routing (react-router) fallback: any request that isn't an
// API route or a real static file resolves to index.html. Must come after
// MapControllers so API routes still take precedence.
app.MapFallbackToFile("index.html");

app.Run();

static string? TryReadPersistedLogLevel(string dbPath)
{
    if (!File.Exists(dbPath))
    {
        return null;
    }

    try
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT LogLevel FROM AdminSettings LIMIT 1";
        return command.ExecuteScalar() as string;
    }
    catch (SqliteException)
    {
        // Table doesn't exist yet (fresh DB, migrations haven't run this
        // process) — nothing persisted yet, that's fine.
        return null;
    }
}

static bool IsDemoModeEnabled()
{
    var value = Environment.GetEnvironmentVariable("UPDATEWATCH2_DEMOMODE");
    return value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
}

static string MapLogLevel(string value) => value.Trim().ToUpperInvariant() switch
{
    "DEBUG" => nameof(LogLevel.Debug),
    "INFO" => nameof(LogLevel.Information),
    "WARNING" => nameof(LogLevel.Warning),
    "ERROR" => nameof(LogLevel.Error),
    _ => value,
};

// Exposes the implicitly generated Program class for WebApplicationFactory<Program> in tests.
public partial class Program;
