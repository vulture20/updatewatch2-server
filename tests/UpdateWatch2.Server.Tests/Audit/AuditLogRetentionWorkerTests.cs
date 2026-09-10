using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Audit;

public class AuditLogRetentionWorkerTests
{
    private readonly FakeAuditLogService _auditLogService = new();
    private readonly FakeAdminSettingsStore _settingsStore = new();
    private readonly ServiceProvider _services;

    public AuditLogRetentionWorkerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuditLogService>(_auditLogService);
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Checks_immediately_on_startup_rather_than_waiting_out_the_first_interval()
    {
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _auditLogService.PurgeCallCount >= 1);
        await worker.StopAsync(CancellationToken.None);

        // Exact, not just >= 1: a 999-hour interval means no second check
        // can plausibly land within this test's lifetime, so once the
        // first has landed the count is stable.
        Assert.Equal(1, _auditLogService.PurgeCallCount);
    }

    [Fact]
    public async Task Re_reads_the_configured_retention_on_every_iteration_rather_than_only_at_startup()
    {
        _settingsStore.AuditLogRetentionDays = 30;
        var worker = CreateWorker(TimeSpan.Zero);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _auditLogService.PurgeCallCount > 1);

        // More than one call proves the loop actually used the near-zero
        // injected interval rather than some large fixed default.
        Assert.True(_auditLogService.PurgeCallCount > 1, $"Expected multiple purges, got {_auditLogService.PurgeCallCount}.");
        Assert.Equal(30, _auditLogService.LastReceivedRetentionDays);

        // Changing the setting mid-run and seeing the very next call pick
        // it up (rather than staying stuck with whatever was captured at
        // startup) is what actually proves this re-reads live, not just
        // that the loop runs more than once — the worker must still be
        // running for that, so StopAsync only happens once this is done.
        _settingsStore.AuditLogRetentionDays = 60;
        await WaitUntilAsync(() => _auditLogService.LastReceivedRetentionDays == 60);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(60, _auditLogService.LastReceivedRetentionDays);
    }

    [Fact]
    public async Task Stops_cleanly_without_throwing_when_cancelled_mid_wait()
    {
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _auditLogService.PurgeCallCount >= 1);
        await worker.StopAsync(CancellationToken.None);

        // No exception propagating out of Start/StopAsync is the assertion.
    }

    [Fact]
    public async Task Continues_the_loop_after_the_service_throws_unexpectedly()
    {
        var throwingService = new ThrowingAuditLogService();
        var services = new ServiceCollection();
        services.AddSingleton<IAuditLogService>(throwingService);
        using var provider = services.BuildServiceProvider();

        var worker = new AuditLogRetentionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), _settingsStore, NullLogger<AuditLogRetentionWorker>.Instance, TimeSpan.Zero);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => throwingService.CallCount > 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(throwingService.CallCount > 1, $"Expected the loop to keep running after a throw, got {throwingService.CallCount} calls.");
    }

    private AuditLogRetentionWorker CreateWorker(TimeSpan checkInterval) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), _settingsStore, NullLogger<AuditLogRetentionWorker>.Instance, checkInterval);

    /// <summary>
    /// Polls <paramref name="condition"/> instead of a single fixed
    /// <c>Task.Delay</c> — same CI-flake fix as <c>AgentUpdateCheckWorkerTests</c>'s
    /// own helper: <c>BackgroundService.StartAsync</c> only schedules
    /// <c>ExecuteAsync</c>, it doesn't wait for that task to actually get
    /// CPU time. Times out silently rather than asserting itself, so the
    /// caller's own assertion reports a useful failure message.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
    }

    private class FakeAuditLogService : IAuditLogService
    {
        public int PurgeCallCount { get; private set; }

        public int LastReceivedRetentionDays { get; private set; } = -1;

        public Task LogAsync(string actor, string action, string? details = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AuditLogPageDto> GetPageAsync(int page, int pageSize, string? search = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> PurgeOlderThanAsync(int retentionDays, CancellationToken ct = default)
        {
            PurgeCallCount++;
            LastReceivedRetentionDays = retentionDays;
            return Task.FromResult(0);
        }
    }

    private class ThrowingAuditLogService : IAuditLogService
    {
        public int CallCount { get; private set; }

        public Task LogAsync(string actor, string action, string? details = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AuditLogPageDto> GetPageAsync(int page, int pageSize, string? search = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> PurgeOlderThanAsync(int retentionDays, CancellationToken ct = default)
        {
            CallCount++;
            throw new InvalidOperationException("simulated failure");
        }
    }
}
