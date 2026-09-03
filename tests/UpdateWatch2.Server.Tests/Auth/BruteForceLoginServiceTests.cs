using System.Net;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Tests.Auth;

public class BruteForceLoginServiceTests
{
    private static BruteForceLoginService CreateService(BruteForceOptions options, string? trustedIpRange = null) =>
        new(new FakeAdminSettingsStore(options), new FakeTrustedIpRangeProvider(trustedIpRange));

    [Fact]
    public void Locks_out_after_max_attempts_within_window()
    {
        var options = new BruteForceOptions { MaxAttempts = 3, WindowMinutes = 5, LockoutMinutes = 30 };
        var service = CreateService(options);
        var ip = IPAddress.Parse("203.0.113.1");

        Assert.False(service.IsLockedOut("admin", ip));

        service.RecordFailedAttempt("admin", ip);
        service.RecordFailedAttempt("admin", ip);
        Assert.False(service.IsLockedOut("admin", ip));

        service.RecordFailedAttempt("admin", ip);
        Assert.True(service.IsLockedOut("admin", ip));
    }

    [Fact]
    public void Successful_login_clears_prior_failed_attempts()
    {
        var options = new BruteForceOptions { MaxAttempts = 3, WindowMinutes = 5, LockoutMinutes = 30 };
        var service = CreateService(options);
        var ip = IPAddress.Parse("203.0.113.1");

        service.RecordFailedAttempt("admin", ip);
        service.RecordFailedAttempt("admin", ip);
        service.RecordSuccessfulLogin("admin", ip);
        service.RecordFailedAttempt("admin", ip);

        Assert.False(service.IsLockedOut("admin", ip));
    }

    [Fact]
    public void Trusted_ip_is_exempt_from_lockout()
    {
        var options = new BruteForceOptions { MaxAttempts = 1, WindowMinutes = 5, LockoutMinutes = 30 };
        var service = CreateService(options, trustedIpRange: "203.0.113.0/24");
        var ip = IPAddress.Parse("203.0.113.42");

        service.RecordFailedAttempt("admin", ip);
        service.RecordFailedAttempt("admin", ip);

        Assert.False(service.IsLockedOut("admin", ip));
    }

    private class FakeTrustedIpRangeProvider(string? trustedIpRange) : ITrustedIpRangeProvider
    {
        public string? TrustedIpRange => trustedIpRange;
    }

    /// <summary>Only <see cref="BruteForce"/> is exercised by this service; everything else throws if touched.</summary>
    private class FakeAdminSettingsStore(BruteForceOptions bruteForce) : IAdminSettingsStore
    {
        public BruteForceOptions BruteForce => bruteForce;

        public SmtpOptions Smtp => throw new NotSupportedException();

        public NotificationThresholdOptions NotificationThresholds => throw new NotSupportedException();

        public AdOptions Ad => throw new NotSupportedException();

        public string LogLevel => throw new NotSupportedException();

        public Task InitializeAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AdminSettingsDto> UpdateAsync(UpdateAdminSettingsRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public AdminSettingsDto ToDto() => throw new NotSupportedException();
    }
}
