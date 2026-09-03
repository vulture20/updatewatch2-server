using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Tests.Admin;

public class AdminSettingsStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-settings-store-test-{Guid.NewGuid()}.sqlite");
    private readonly ServiceProvider _services;

    public AdminSettingsStoreTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={_dbPath}"));
        services.Configure<BruteForceOptions>(o => { o.MaxAttempts = 6; o.WindowMinutes = 5; o.LockoutMinutes = 30; });
        services.Configure<SmtpOptions>(o => { o.Host = ""; o.Port = 587; o.FromAddress = ""; o.FromName = "UpdateWatch2"; });
        services.Configure<NotificationThresholdOptions>(o => { o.UpdatesPerMachine = 5; o.AffectedMachines = 10; });
        services.AddSingleton<IAdminSettingsStore, AdminSettingsStore>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    public void Dispose()
    {
        _services.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task InitializeAsync_seeds_one_row_from_the_configured_defaults()
    {
        var store = _services.GetRequiredService<IAdminSettingsStore>();

        await store.InitializeAsync();

        Assert.Equal(6, store.BruteForce.MaxAttempts);
        Assert.Equal(5, store.BruteForce.WindowMinutes);
        Assert.Equal(30, store.BruteForce.LockoutMinutes);

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.AdminSettings.CountAsync());
    }

    [Fact]
    public async Task InitializeAsync_is_idempotent()
    {
        var store = _services.GetRequiredService<IAdminSettingsStore>();

        await store.InitializeAsync();
        await store.InitializeAsync();

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.AdminSettings.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_persists_and_immediately_updates_the_live_cache()
    {
        var store = _services.GetRequiredService<IAdminSettingsStore>();
        await store.InitializeAsync();

        var request = new UpdateAdminSettingsRequest(
            LogLevel: "DEBUG",
            BruteForceMaxAttempts: 10,
            BruteForceWindowMinutes: 15,
            BruteForceLockoutMinutes: 60,
            SmtpHost: "smtp.example.com",
            SmtpPort: 25,
            SmtpUsername: "notifier",
            SmtpPassword: "s3cret!",
            SmtpEncryption: "SslTls",
            SmtpFromAddress: "updatewatch2@example.com",
            SmtpFromName: "UpdateWatch2 Notifier",
            NotificationUpdatesPerMachineThreshold: 3,
            NotificationAffectedMachinesThreshold: 7);

        var dto = await store.UpdateAsync(request);

        // Reflected in the live cache immediately, no restart needed.
        Assert.Equal(10, store.BruteForce.MaxAttempts);
        Assert.Equal("smtp.example.com", store.Smtp.Host);
        Assert.Equal(SmtpEncryption.SslTls, store.Smtp.Encryption);
        Assert.Equal("s3cret!", store.Smtp.Password);
        Assert.Equal("DEBUG", store.LogLevel);
        Assert.Equal(3, store.NotificationThresholds.UpdatesPerMachine);

        // The password never comes back out through the DTO.
        Assert.True(dto.SmtpPasswordSet);
        Assert.Equal("smtp.example.com", dto.SmtpHost);
        Assert.True(dto.SmtpConfigured);

        // ... and survives a fresh read from the DB (not just the cache).
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AdminSettings.SingleAsync();
        Assert.Equal("smtp.example.com", row.SmtpHost);
        Assert.Equal("s3cret!", row.SmtpPassword);
    }

    [Fact]
    public async Task UpdateAsync_with_a_null_password_leaves_the_stored_password_unchanged()
    {
        var store = _services.GetRequiredService<IAdminSettingsStore>();
        await store.InitializeAsync();
        await store.UpdateAsync(BaseRequest() with { SmtpPassword = "initial-password" });

        await store.UpdateAsync(BaseRequest() with { SmtpPassword = null, SmtpHost = "changed.example.com" });

        Assert.Equal("initial-password", store.Smtp.Password);
        Assert.Equal("changed.example.com", store.Smtp.Host);
    }

    [Fact]
    public async Task UpdateAsync_with_an_empty_password_clears_it()
    {
        var store = _services.GetRequiredService<IAdminSettingsStore>();
        await store.InitializeAsync();
        await store.UpdateAsync(BaseRequest() with { SmtpPassword = "initial-password" });

        var dto = await store.UpdateAsync(BaseRequest() with { SmtpPassword = "" });

        Assert.Null(store.Smtp.Password);
        Assert.False(dto.SmtpPasswordSet);
    }

    private static UpdateAdminSettingsRequest BaseRequest() => new(
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
