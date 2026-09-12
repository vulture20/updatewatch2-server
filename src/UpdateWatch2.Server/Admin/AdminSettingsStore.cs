using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UpdateWatch2.Server.AgentUpdates;
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
    IOptions<CertificateOptions> defaultCertificate,
    IOptions<AgentAutoUpdateOptions> defaultAgentAutoUpdate,
    IConfiguration? configuration = null) : IAdminSettingsStore
{
    private readonly object _lock = new();

    private BruteForceOptions _bruteForce = defaultBruteForce.Value;
    private SmtpOptions _smtp = defaultSmtp.Value;
    private NotificationThresholdOptions _notificationThresholds = defaultNotificationThresholds.Value;
    private AdOptions _ad = defaultAd.Value;
    private CertificateOptions _certificate = defaultCertificate.Value;
    private AgentAutoUpdateOptions _agentAutoUpdate = defaultAgentAutoUpdate.Value;
    private string _logLevel = "INFO";
    private int _auditLogRetentionDays = DefaultAuditLogRetentionDays;

    private const int DefaultAuditLogRetentionDays = 90;

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

    public AgentAutoUpdateOptions AgentAutoUpdate
    {
        get { lock (_lock) return _agentAutoUpdate; }
    }

    public string LogLevel
    {
        get { lock (_lock) return _logLevel; }
    }

    public int AuditLogRetentionDays
    {
        get { lock (_lock) return _auditLogRetentionDays; }
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
        row.NotificationRecipientAddress = string.IsNullOrWhiteSpace(request.NotificationRecipientAddress) ? null : request.NotificationRecipientAddress;
        row.NotificationUpdatesPerMachineThreshold = request.NotificationUpdatesPerMachineThreshold;
        row.NotificationUpdatesPerMachineEnabled = request.NotificationUpdatesPerMachineEnabled;
        row.NotificationAffectedMachinesThreshold = request.NotificationAffectedMachinesThreshold;
        row.NotificationAffectedMachinesEnabled = request.NotificationAffectedMachinesEnabled;
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
        row.AgentAutoUpdateEnabled = request.AgentAutoUpdateEnabled;
        if (request.GitHubToken is not null)
        {
            row.GitHubToken = request.GitHubToken.Length == 0 ? null : request.GitHubToken;
        }
        row.AgentAutoUpdateCheckIntervalHours = request.AgentAutoUpdateCheckIntervalHours;
        row.AuditLogRetentionDays = request.AuditLogRetentionDays;
        row.CertificateExpiryWarningLeadDays = request.CertificateExpiryWarningLeadDays;
        row.CertificateExpiryNotificationsEnabled = request.CertificateExpiryNotificationsEnabled;
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
                _smtp.NotificationRecipientAddress,
                _smtp.IsConfigured,
                _notificationThresholds.UpdatesPerMachine,
                _notificationThresholds.UpdatesPerMachineEnabled,
                _notificationThresholds.AffectedMachines,
                _notificationThresholds.AffectedMachinesEnabled,
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
                _certificate.AgentCertificateValidityDays,
                _agentAutoUpdate.Enabled,
                !string.IsNullOrEmpty(_agentAutoUpdate.GitHubToken),
                _agentAutoUpdate.CheckIntervalHours,
                _auditLogRetentionDays,
                _certificate.CertificateExpiryWarningLeadDays,
                _certificate.CertificateExpiryNotificationsEnabled);
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
        NotificationRecipientAddress = defaultSmtp.Value.NotificationRecipientAddress,
        NotificationUpdatesPerMachineThreshold = defaultNotificationThresholds.Value.UpdatesPerMachine,
        NotificationUpdatesPerMachineEnabled = defaultNotificationThresholds.Value.UpdatesPerMachineEnabled,
        NotificationAffectedMachinesThreshold = defaultNotificationThresholds.Value.AffectedMachines,
        NotificationAffectedMachinesEnabled = defaultNotificationThresholds.Value.AffectedMachinesEnabled,
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
        AgentAutoUpdateEnabled = defaultAgentAutoUpdate.Value.Enabled,
        GitHubToken = defaultAgentAutoUpdate.Value.GitHubToken,
        AgentAutoUpdateCheckIntervalHours = defaultAgentAutoUpdate.Value.CheckIntervalHours,
        AuditLogRetentionDays = DefaultAuditLogRetentionDays,
        CertificateExpiryWarningLeadDays = defaultCertificate.Value.CertificateExpiryWarningLeadDays,
        CertificateExpiryNotificationsEnabled = defaultCertificate.Value.CertificateExpiryNotificationsEnabled,
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
            NotificationRecipientAddress = row.NotificationRecipientAddress,
        };
        var thresholds = new NotificationThresholdOptions
        {
            UpdatesPerMachine = row.NotificationUpdatesPerMachineThreshold,
            UpdatesPerMachineEnabled = row.NotificationUpdatesPerMachineEnabled,
            AffectedMachines = row.NotificationAffectedMachinesThreshold,
            AffectedMachinesEnabled = row.NotificationAffectedMachinesEnabled,
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
            CertificateExpiryWarningLeadDays = row.CertificateExpiryWarningLeadDays,
            CertificateExpiryNotificationsEnabled = row.CertificateExpiryNotificationsEnabled,
        };
        var agentAutoUpdate = new AgentAutoUpdateOptions
        {
            Enabled = row.AgentAutoUpdateEnabled,
            GitHubToken = row.GitHubToken,
            CheckIntervalHours = row.AgentAutoUpdateCheckIntervalHours,
        };

        lock (_lock)
        {
            _bruteForce = bruteForce;
            _smtp = smtp;
            _notificationThresholds = thresholds;
            _ad = ad;
            _certificate = certificate;
            _agentAutoUpdate = agentAutoUpdate;
            _logLevel = row.LogLevel;
            _auditLogRetentionDays = row.AuditLogRetentionDays;
        }

        // Pushes the change to the ACTUAL running logger, not just this
        // store's own cache — closing the one long-standing exception to
        // "every admin setting takes effect immediately" this class's own
        // interface doc comment used to call out (found by a user report:
        // changing this in the UI had no effect on a running container's
        // `docker logs`, "just" needing a restart no other setting here
        // does). `configuration` is the same `IConfiguration`/`ConfigurationManager`
        // instance `Program.cs` set `Logging:LogLevel:Default` on before
        // `builder.Build()`.
        //
        // The indexer write alone is NOT enough post-startup — confirmed
        // by hand with a throwaway harness, not from documentation: unlike
        // `appsettings.json`'s own `reloadOnChange`, mutating a value
        // through `ConfigurationRoot`'s indexer never raises that root's
        // own reload/change token by itself (only an underlying provider's
        // *own* reload mechanism — e.g. a file watcher — or an explicit
        // `IConfigurationRoot.Reload()` call does), and the logging
        // subsystem's `IOptionsMonitor<LoggerFilterOptions>` only
        // re-evaluates `Logging:LogLevel:*` when that token fires. Before
        // `Reload()` was added here, `ILogger.IsEnabled(...)` never
        // changed after the very first read — this is exactly why
        // `Program.cs`'s OWN pre-`Build()` indexer write "just works" with
        // no `Reload()` call of its own: nothing has read/cached
        // `LoggerFilterOptions` yet at that point (the host isn't even
        // built), so the mutated value is simply what's picked up on that
        // eventual first read — a fundamentally different situation from
        // changing an ALREADY-cached value on a fully running host, which
        // this call site is. Null only in the one test that constructs
        // this class directly without DI (`LegacyAdminSettingsMigrationTests`);
        // every real path resolves the genuine `IConfiguration` instead.
        if (configuration is not null && LogLevelMapper.IsValid(row.LogLevel))
        {
            configuration["Logging:LogLevel:Default"] = LogLevelMapper.ToConfigurationValue(row.LogLevel);
            if (configuration is IConfigurationRoot root)
            {
                root.Reload();
            }
        }
    }
}
