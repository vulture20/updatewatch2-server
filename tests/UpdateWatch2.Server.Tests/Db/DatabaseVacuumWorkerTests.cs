using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Db;

namespace UpdateWatch2.Server.Tests.Db;

public class DatabaseVacuumWorkerTests
{
    private readonly FakeDatabaseMaintenanceService _maintenance = new();
    private readonly ServiceProvider _services;

    public DatabaseVacuumWorkerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDatabaseMaintenanceService>(_maintenance);
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Checks_immediately_on_startup_rather_than_waiting_out_the_first_interval()
    {
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _maintenance.IncrementalVacuumCallCount >= 1);
        await worker.StopAsync(CancellationToken.None);

        // Exact, not just >= 1: a 999-hour interval means no second check
        // can plausibly land within this test's lifetime, so once the
        // first has landed the count is stable.
        Assert.Equal(1, _maintenance.IncrementalVacuumCallCount);
    }

    [Fact]
    public async Task Stops_cleanly_without_throwing_when_cancelled_mid_wait()
    {
        var worker = CreateWorker(TimeSpan.FromHours(999));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _maintenance.IncrementalVacuumCallCount >= 1);
        await worker.StopAsync(CancellationToken.None);

        // No exception propagating out of Start/StopAsync is the assertion.
    }

    [Fact]
    public async Task Continues_the_loop_after_the_service_throws_unexpectedly()
    {
        var throwingService = new ThrowingDatabaseMaintenanceService();
        var services = new ServiceCollection();
        services.AddSingleton<IDatabaseMaintenanceService>(throwingService);
        using var provider = services.BuildServiceProvider();

        var worker = new DatabaseVacuumWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DatabaseVacuumWorker>.Instance, TimeSpan.Zero);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => throwingService.CallCount > 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(throwingService.CallCount > 1, $"Expected the loop to keep running after a throw, got {throwingService.CallCount} calls.");
    }

    private DatabaseVacuumWorker CreateWorker(TimeSpan checkInterval) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), NullLogger<DatabaseVacuumWorker>.Instance, checkInterval);

    /// <summary>
    /// Polls <paramref name="condition"/> instead of a single fixed
    /// <c>Task.Delay</c> — same CI-flake fix as this project's other
    /// worker tests' own helper: <c>BackgroundService.StartAsync</c> only
    /// schedules <c>ExecuteAsync</c>, it doesn't wait for that task to
    /// actually get CPU time. Times out silently rather than asserting
    /// itself, so the caller's own assertion reports a useful message.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
    }

    private class FakeDatabaseMaintenanceService : IDatabaseMaintenanceService
    {
        public int IncrementalVacuumCallCount { get; private set; }

        public Task EnsureIncrementalAutoVacuumEnabledAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task IncrementalVacuumAsync(CancellationToken ct = default)
        {
            IncrementalVacuumCallCount++;
            return Task.CompletedTask;
        }
    }

    private class ThrowingDatabaseMaintenanceService : IDatabaseMaintenanceService
    {
        public int CallCount { get; private set; }

        public Task EnsureIncrementalAutoVacuumEnabledAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task IncrementalVacuumAsync(CancellationToken ct = default)
        {
            CallCount++;
            throw new InvalidOperationException("simulated failure");
        }
    }
}
