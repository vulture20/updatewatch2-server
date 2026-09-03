using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Db;
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

// These three are bound purely as the compiled-in defaults AdminSettingsStore
// seeds its DB row from on first run — the database is authoritative after
// that, not these IOptions<T> snapshots. See each options class's doc comment.
builder.Services.Configure<BruteForceOptions>(builder.Configuration.GetSection(BruteForceOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<NotificationThresholdOptions>(builder.Configuration.GetSection(NotificationThresholdOptions.SectionName));

builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<IUpdateService, UpdateService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddSingleton<ITrustedIpRangeProvider, EnvironmentTrustedIpRangeProvider>();
builder.Services.AddSingleton<IBruteForceLoginService, BruteForceLoginService>();
builder.Services.AddScoped<IEmailNotificationService, EmailNotificationService>();
builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
builder.Services.AddSingleton<IAdminSettingsStore, AdminSettingsStore>();

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

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "UpdateWatch2.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Relaxed in Development so the API can be exercised over plain
        // HTTP locally without a trusted dev certificate; real deployments
        // always require HTTPS, so this stays Always outside Development.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
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
    });
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var accounts = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
    await accounts.EnsureSeededAsync();

    var settingsStore = scope.ServiceProvider.GetRequiredService<IAdminSettingsStore>();
    await settingsStore.InitializeAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

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
