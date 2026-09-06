using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.AgentUpdates;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.AgentUpdates;

public class AgentUpdateCheckWorkerTests
{
    private readonly FakeAgentUpdateService _agentUpdateService = new();
    private readonly FakeAdminSettingsStore _settingsStore = new();
    private readonly ServiceProvider _services;

    public AgentUpdateCheckWorkerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgentUpdateService>(_agentUpdateService);
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Checks_immediately_on_startup_rather_than_waiting_out_the_first_interval()
    {
        _settingsStore.AgentAutoUpdate = new AgentAutoUpdateOptions { CheckIntervalHours = 999 };
        var worker = CreateWorker();

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, _agentUpdateService.CheckCallCount);
    }

    [Fact]
    public async Task Re_reads_the_configured_interval_on_every_iteration_rather_than_only_at_startup()
    {
        // Zero hours is never reachable through the validated admin API
        // (PUT /api/admin/settings rejects anything below 1) — used here
        // purely to make Task.Delay resolve near-instantly, so this test
        // can observe several real loop iterations without an actual
        // multi-hour wait.
        _settingsStore.AgentAutoUpdate = new AgentAutoUpdateOptions { CheckIntervalHours = 0 };
        var worker = CreateWorker();

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);

        // More than one call proves the loop actually used the small
        // configured interval rather than some large fixed default.
        Assert.True(_agentUpdateService.CheckCallCount > 1, $"Expected multiple checks, got {_agentUpdateService.CheckCallCount}.");
    }

    [Fact]
    public async Task Stops_cleanly_without_throwing_when_cancelled_mid_wait()
    {
        _settingsStore.AgentAutoUpdate = new AgentAutoUpdateOptions { CheckIntervalHours = 999 };
        var worker = CreateWorker();

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(20);
        await worker.StopAsync(CancellationToken.None);

        // No exception propagating out of Start/StopAsync is the assertion —
        // OperationCanceledException from the interval wait must be caught
        // internally, not surfaced to the host.
    }

    [Fact]
    public async Task Continues_the_loop_after_the_service_throws_unexpectedly()
    {
        var throwingService = new ThrowingAgentUpdateService();
        var services = new ServiceCollection();
        services.AddSingleton<IAgentUpdateService>(throwingService);
        using var provider = services.BuildServiceProvider();

        _settingsStore.AgentAutoUpdate = new AgentAutoUpdateOptions { CheckIntervalHours = 0 };
        var worker = new AgentUpdateCheckWorker(provider.GetRequiredService<IServiceScopeFactory>(), _settingsStore, NullLogger<AgentUpdateCheckWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(throwingService.CallCount > 1, $"Expected the loop to keep running after a throw, got {throwingService.CallCount} calls.");
    }

    private AgentUpdateCheckWorker CreateWorker() =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), _settingsStore, NullLogger<AgentUpdateCheckWorker>.Instance);

    private class ThrowingAgentUpdateService : IAgentUpdateService
    {
        public int CallCount { get; private set; }

        public bool IsEnabled => true;

        public Task<AgentUpdateCheckOutcome> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            CallCount++;
            throw new InvalidOperationException("simulated failure");
        }

        public Task<AgentUpdateOffer?> GetOfferForAsync(string? currentAgentVersion, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AgentUpdateStatusDto> GetStatusAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<string?> ResolveDownloadPathAsync(string fileName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
