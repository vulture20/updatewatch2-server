using System.Net;
using Microsoft.Extensions.Options;
using UpdateWatch2.Server.Auth;

namespace UpdateWatch2.Server.Tests.Auth;

public class BruteForceLoginServiceTests
{
    private static BruteForceLoginService CreateService(BruteForceOptions options) =>
        new(TestOptionsMonitor.For(options));

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
        var options = new BruteForceOptions { MaxAttempts = 1, WindowMinutes = 5, LockoutMinutes = 30, TrustedIpRange = "203.0.113.0/24" };
        var service = CreateService(options);
        var ip = IPAddress.Parse("203.0.113.42");

        service.RecordFailedAttempt("admin", ip);
        service.RecordFailedAttempt("admin", ip);

        Assert.False(service.IsLockedOut("admin", ip));
    }

    private class TestOptionsMonitor : IOptionsMonitor<BruteForceOptions>
    {
        public required BruteForceOptions CurrentValue { get; init; }

        public static TestOptionsMonitor For(BruteForceOptions value) => new() { CurrentValue = value };

        public BruteForceOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<BruteForceOptions, string?> listener) => NullDisposable.Instance;

        private class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
