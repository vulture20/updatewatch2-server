using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Tests.Admin;

/// <summary>
/// Regression test for a real bug reported against a live deployment: the
/// AddActiveDirectorySettings migration ALTERed an already-existing
/// AdminSettings table and backfilled its new columns on any pre-existing
/// row (i.e. any server installed before AD login existed) with SQL-level
/// defaults that don't match AdOptions' actual C# defaults — AdEncryption
/// got "" instead of "StartTls". AdminSettingsStore.Apply unconditionally
/// does Enum.Parse&lt;AdEncryption&gt;(row.AdEncryption), which throws
/// ArgumentException("Must specify valid information for parsing in the
/// string.") for the empty string, crashing the whole app at startup for
/// anyone upgrading an existing deployment.
///
/// This reproduces that exact scenario — migrate only up through
/// AddAdminSettings, insert a row shaped like what SeedFromDefaults wrote
/// back when only the pre-AD columns existed, then apply the remaining
/// migrations (AddActiveDirectorySettings, which corrupts it, followed by
/// FixLegacyAdSettingsDefaults, which should repair it) — rather than
/// asserting anything about the fix migration in isolation, since what
/// actually matters is the end-to-end startup path
/// (InitializeAsync -&gt; Apply) not throwing.
/// </summary>
public class LegacyAdminSettingsMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-legacy-ad-settings-test-{Guid.NewGuid()}.sqlite");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Upgrading_a_pre_AD_login_database_does_not_crash_on_startup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;

        await using (var db = new AppDbContext(options))
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();

            // Stop right after AddAdminSettings — before any AD column exists.
            await migrator.MigrateAsync("20260903172334_AddAdminSettings");

            // Exactly what SeedFromDefaults wrote back when the AdminSettings
            // table only had the pre-AD columns.
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "AdminSettings"
                    ("LogLevel", "BruteForceMaxAttempts", "BruteForceWindowMinutes", "BruteForceLockoutMinutes",
                     "SmtpHost", "SmtpPort", "SmtpUsername", "SmtpPassword", "SmtpEncryption", "SmtpFromAddress", "SmtpFromName",
                     "NotificationUpdatesPerMachineThreshold", "NotificationAffectedMachinesThreshold", "UpdatedAt")
                VALUES
                    ('INFO', 6, 5, 30, '', 587, NULL, NULL, 'StartTls', '', 'UpdateWatch2', 5, 10, '2026-01-01T00:00:00+00:00');
                """);

            // The rest, including the migration that corrupts the row
            // (AddActiveDirectorySettings) and the one that repairs it
            // (FixLegacyAdSettingsDefaults).
            await migrator.MigrateAsync();
        }

        await using var freshDb = new AppDbContext(options);
        var store = new AdminSettingsStore(
            new FakeScopeFactory(options),
            FakeOptions.Of(new BruteForceOptions()),
            FakeOptions.Of(new SmtpOptions { FromName = "UpdateWatch2" }),
            FakeOptions.Of(new NotificationThresholdOptions()),
            FakeOptions.Of(new AdOptions()));

        // The regression: this must not throw.
        await store.InitializeAsync();

        Assert.Equal(AdEncryption.StartTls, store.Ad.Encryption);
        Assert.Equal(389, store.Ad.Port);
        Assert.Equal("(&(objectClass=user)(sAMAccountName={0}))", store.Ad.UserSearchFilter);
    }

    private class FakeScopeFactory(DbContextOptions<AppDbContext> options) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FakeScope(options);
    }

    private class FakeScope(DbContextOptions<AppDbContext> options) : IServiceScope
    {
        public IServiceProvider ServiceProvider => new FakeServiceProvider(options);

        public void Dispose()
        {
        }
    }

    private class FakeServiceProvider(DbContextOptions<AppDbContext> options) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(AppDbContext) ? new AppDbContext(options) : null;
    }

    private static class FakeOptions
    {
        public static IOptions<T> Of<T>(T value) where T : class => Options.Create(value);
    }
}
