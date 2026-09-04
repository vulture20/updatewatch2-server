using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Admin;

public class AdminSettingsStore(
    IServiceScopeFactory scopeFactory,
    IOptions<BruteForceOptions> defaultBruteForce,
    IOptions<SmtpOptions> defaultSmtp,
    IOptions<NotificationThresholdOptions> defaultNotificationThresholds,
    IOptions<AdOptions> defaultAd,
    IOptions<CertificateOptions> defaultCertificate) : IAdminSettingsStore
{
    private readonly object _lock = new();

    private BruteForceOptions _bruteForce = defaultBruteForce.Value;
    private SmtpOptions _smtp = defaultSmtp.Value;
    private NotificationThresholdOptions _notificationThresholds = defaultNotificationThresholds.Value;
    private AdOptions _ad = defaultAd.Value;
    private CertificateOptions _certificate = defaultCertificate.Value;
    private string _logLevel = "INFO";

    public BruteForceOptions BruteForce
    {
        get { lock (_lock) return _bruteForce; }
    }

    public SmtpOptions Smtp
    {
        get { lock (_lock) return _smtp; }
    }

    public NotificationThresholdOptions NotificationThresholds
    {
        get { lock (_lock) return _notificationThresholds; }
    }

    public AdOptions Ad
    {
        get { lock (_lock) return _ad; }
    }

    public CertificateOptions Certificate
    {
        get { lock (_lock) return _certificate; }
    }

    public string LogLevel
    {
        get { lock (_lock) return _logLevel; }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.AdminSettings.SingleOrDefaultAsync(ct);
        if (row is null)
        {
            row = SeedFromDefaults();
            db.AdminSettings.Add(row);
            await db.SaveChangesAsync(ct);
        }

        Apply(row);
    }

    public async Task<AdminSettingsDto> UpdateAsync(UpdateAdminSettingsRequest request, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // InitializeAsync runs at startup and guarantees exactly one row —
        // if this throws, something skipped that step (e.g. a test).
        var row = await db.AdminSettings.SingleAsync(ct);

        row.LogLevel = request.LogLevel;
        row.BruteForceMaxAttempts = request.BruteForceMaxAttempts;
        row.BruteForceWindowMinutes = request.BruteForceWindowMinutes;
        row.BruteForceLockoutMinutes = request.BruteForceLockoutMinutes;
        row.SmtpHost = request.SmtpHost;
        row.SmtpPort = request.SmtpPort;
        row.SmtpUsername = request.SmtpUsername;
        if (request.SmtpPassword is not null)
        {
            // Empty string clears it; null (the default when the field is
            // omitted) leaves whatever's already stored untouched — see
            // UpdateAdminSettingsRequest's doc comment.
            row.SmtpPassword = request.SmtpPassword.Length == 0 ? null : request.SmtpPassword;
        }
        row.SmtpEncryption = request.SmtpEncryption;
        row.SmtpFromAddress = request.SmtpFromAddress;
        row.SmtpFromName = request.SmtpFromName;
        row.NotificationUpdatesPerMachineThreshold = request.NotificationUpdatesPerMachineThreshold;
        row.NotificationAffectedMachinesThreshold = request.NotificationAffectedMachinesThreshold;
        row.AdEnabled = request.AdEnabled;
        row.AdHost = request.AdHost;
        row.AdPort = request.AdPort;
        row.AdEncryption = request.AdEncryption;
        row.AdBindDn = request.AdBindDn;
        if (request.AdBindPassword is not null)
        {
            row.AdBindPassword = request.AdBindPassword.Length == 0 ? null : request.AdBindPassword;
        }
        row.AdBaseDn = request.AdBaseDn;
        row.AdUserSearchFilter = request.AdUserSearchFilter;
        row.AdLoginGroupDn = request.AdLoginGroupDn;
        row.AgentCertificateValidityDays = request.AgentCertificateValidityDays;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        Apply(row);
        return ToDto();
    }

    public AdminSettingsDto ToDto()
    {
        lock (_lock)
        {
            return new AdminSettingsDto(
                _logLevel,
                _bruteForce.MaxAttempts,
                _bruteForce.WindowMinutes,
                _bruteForce.LockoutMinutes,
                _smtp.Host,
                _smtp.Port,
                _smtp.Username,
                !string.IsNullOrEmpty(_smtp.Password),
                _smtp.Encryption.ToString(),
                _smtp.FromAddress,
                _smtp.FromName,
                _smtp.IsConfigured,
                _notificationThresholds.UpdatesPerMachine,
                _notificationThresholds.AffectedMachines,
                _ad.Enabled,
                _ad.Host,
                _ad.Port,
                _ad.Encryption.ToString(),
                _ad.BindDn,
                !string.IsNullOrEmpty(_ad.BindPassword),
                _ad.BaseDn,
                _ad.UserSearchFilter,
                _ad.LoginGroupDn,
                _ad.IsConfigured,
                _certificate.AgentCertificateValidityDays);
        }
    }

    private AdminSettings SeedFromDefaults() => new()
    {
        // UPDATEWATCH2_LOGLEVEL, if set, wins over appsettings.json's
        // default here too — consistent with how Program.cs treats it as
        // the higher-priority source for the log level actually applied
        // at startup.
        LogLevel = (Environment.GetEnvironmentVariable("UPDATEWATCH2_LOGLEVEL") ?? "INFO").ToUpperInvariant(),
        BruteForceMaxAttempts = defaultBruteForce.Value.MaxAttempts,
        BruteForceWindowMinutes = defaultBruteForce.Value.WindowMinutes,
        BruteForceLockoutMinutes = defaultBruteForce.Value.LockoutMinutes,
        SmtpHost = defaultSmtp.Value.Host,
        SmtpPort = defaultSmtp.Value.Port,
        SmtpUsername = defaultSmtp.Value.Username,
        SmtpPassword = defaultSmtp.Value.Password,
        SmtpEncryption = defaultSmtp.Value.Encryption.ToString(),
        SmtpFromAddress = defaultSmtp.Value.FromAddress,
        SmtpFromName = defaultSmtp.Value.FromName,
        NotificationUpdatesPerMachineThreshold = defaultNotificationThresholds.Value.UpdatesPerMachine,
        NotificationAffectedMachinesThreshold = defaultNotificationThresholds.Value.AffectedMachines,
        AdEnabled = defaultAd.Value.Enabled,
        AdHost = defaultAd.Value.Host,
        AdPort = defaultAd.Value.Port,
        AdEncryption = defaultAd.Value.Encryption.ToString(),
        AdBindDn = defaultAd.Value.BindDn,
        AdBindPassword = defaultAd.Value.BindPassword,
        AdBaseDn = defaultAd.Value.BaseDn,
        AdUserSearchFilter = defaultAd.Value.UserSearchFilter,
        AdLoginGroupDn = defaultAd.Value.LoginGroupDn,
        AgentCertificateValidityDays = defaultCertificate.Value.AgentCertificateValidityDays,
    };

    private void Apply(AdminSettings row)
    {
        var bruteForce = new BruteForceOptions
        {
            MaxAttempts = row.BruteForceMaxAttempts,
            WindowMinutes = row.BruteForceWindowMinutes,
            LockoutMinutes = row.BruteForceLockoutMinutes,
        };
        var smtp = new SmtpOptions
        {
            Host = row.SmtpHost,
            Port = row.SmtpPort,
            Username = row.SmtpUsername,
            Password = row.SmtpPassword,
            Encryption = Enum.Parse<SmtpEncryption>(row.SmtpEncryption),
            FromAddress = row.SmtpFromAddress,
            FromName = row.SmtpFromName,
        };
        var thresholds = new NotificationThresholdOptions
        {
            UpdatesPerMachine = row.NotificationUpdatesPerMachineThreshold,
            AffectedMachines = row.NotificationAffectedMachinesThreshold,
        };
        var ad = new AdOptions
        {
            Enabled = row.AdEnabled,
            Host = row.AdHost,
            Port = row.AdPort,
            Encryption = Enum.Parse<AdEncryption>(row.AdEncryption),
            BindDn = row.AdBindDn,
            BindPassword = row.AdBindPassword,
            BaseDn = row.AdBaseDn,
            UserSearchFilter = row.AdUserSearchFilter,
            LoginGroupDn = row.AdLoginGroupDn,
        };
        var certificate = new CertificateOptions
        {
            AgentCertificateValidityDays = row.AgentCertificateValidityDays,
        };

        lock (_lock)
        {
            _bruteForce = bruteForce;
            _smtp = smtp;
            _notificationThresholds = thresholds;
            _ad = ad;
            _certificate = certificate;
            _logLevel = row.LogLevel;
        }
    }
}
