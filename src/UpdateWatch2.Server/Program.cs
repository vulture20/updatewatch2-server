using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Notifications;
using UpdateWatch2.Server.Updates;

var builder = WebApplication.CreateBuilder(args);

// UPDATEWATCH2_LOGLEVEL overrides the configured default log level, per
// CLAUDE.md ("Server: over the Oberfläche oder über die Umgebungsvariable
// UPDATEWATCH2_LOGLEVEL").
var logLevelEnv = Environment.GetEnvironmentVariable("UPDATEWATCH2_LOGLEVEL");
if (logLevelEnv is not null && Enum.TryParse<LogLevel>(MapLogLevel(logLevelEnv), out var minLevel))
{
    builder.Logging.SetMinimumLevel(minLevel);
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

builder.Services.Configure<BruteForceOptions>(builder.Configuration.GetSection(BruteForceOptions.SectionName));
// UPDATEWATCH2_TRUSTEDIP is read directly from the environment rather than
// appsettings, per CLAUDE.md — it's an operational/deployment concern, not
// an admin-UI setting.
builder.Services.PostConfigure<BruteForceOptions>(opts =>
    opts.TrustedIpRange = Environment.GetEnvironmentVariable("UPDATEWATCH2_TRUSTEDIP"));

builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<NotificationThresholdOptions>(builder.Configuration.GetSection(NotificationThresholdOptions.SectionName));

builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<IUpdateService, UpdateService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddSingleton<IBruteForceLoginService, BruteForceLoginService>();
builder.Services.AddScoped<IEmailNotificationService, EmailNotificationService>();
builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();

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
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

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
