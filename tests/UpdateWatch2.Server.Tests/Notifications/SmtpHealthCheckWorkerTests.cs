using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Tests.Notifications;

public class SmtpHealthCheckWorkerTests
{
    private readonly FakeEmailNotificationService _emailService = new();
    private readonly SmtpHealthCache _cache = new();
    private readonly ServiceProvider _services;

    public SmtpHealthCheckWorkerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEmailNotificationService>(_emailService);
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Checks_immediately_on_startup_and_populates_the_cache()
    {
        _emailService.NextResult = true;
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _cache.CheckedAt is not null);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(_cache.IsHealthy);
        Assert.Equal(1, _emailService.CallCount);
    }

    [Fact]
    public async Task Caches_an_unhealthy_result_the_same_way_as_a_healthy_one()
    {
        _emailService.NextResult = false;
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _cache.CheckedAt is not null);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(_cache.IsHealthy);
    }

    [Fact]
    public async Task Refreshes_the_cache_again_on_the_next_tick_when_the_result_changes()
    {
        _emailService.NextResult = false;
        var worker = CreateWorker(TimeSpan.FromMilliseconds(20));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _cache.CheckedAt is not null);
        Assert.False(_cache.IsHealthy);

        _emailService.NextResult = true;
        await WaitUntilAsync(() => _cache.IsHealthy);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(_cache.IsHealthy);
    }

    [Fact]
    public async Task Leaves_the_cache_unchanged_when_the_check_throws_unexpectedly()
    {
        _emailService.ThrowOnCheck = true;
        var worker = CreateWorker(TimeSpan.FromMilliseconds(20));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _emailService.CallCount >= 1);
        // A couple more ticks pass, still throwing — the cache should
        // still read as "never actually checked" (CheckedAt null), not
        // silently flip to false as if a real health check had run.
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        Assert.Null(_cache.CheckedAt);
        Assert.True(_emailService.CallCount > 1, $"Expected the loop to keep running after a throw, got {_emailService.CallCount} calls.");
    }

    private SmtpHealthCheckWorker CreateWorker(TimeSpan checkInterval) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), _cache, NullLogger<SmtpHealthCheckWorker>.Instance, checkInterval);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
    }

    private class FakeEmailNotificationService : IEmailNotificationService
    {
        public bool NextResult { get; set; }

        public bool ThrowOnCheck { get; set; }

        public int CallCount { get; private set; }

        public Task SendTestEmailAsync(string toAddress, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> IsHealthyAsync(CancellationToken ct = default)
        {
            CallCount++;
            if (ThrowOnCheck)
            {
                throw new InvalidOperationException("simulated failure");
            }

            return Task.FromResult(NextResult);
        }

        public Task SendNotificationAsync(string toAddress, string subjectEn, string bodyEn, string subjectDe, string bodyDe, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
