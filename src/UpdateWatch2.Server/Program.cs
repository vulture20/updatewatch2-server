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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
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
