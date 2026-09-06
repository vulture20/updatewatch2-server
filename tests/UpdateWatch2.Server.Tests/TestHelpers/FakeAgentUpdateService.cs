using UpdateWatch2.Server.AgentUpdates;

namespace UpdateWatch2.Server.Tests.TestHelpers;

/// <summary>
/// A minimal hand-written IAgentUpdateService fake — defaults to "nothing
/// to offer" (matching this feature's own real default of having no
/// known release until the background worker actually checks GitHub),
/// settable per test via <see cref="Offer"/>.
/// </summary>
public class FakeAgentUpdateService : IAgentUpdateService
{
    public bool IsEnabled { get; set; } = true;

    public AgentUpdateOffer? Offer { get; set; }

    public AgentUpdateCheckOutcome CheckOutcome { get; set; } = AgentUpdateCheckOutcome.UpToDate;

    public int CheckCallCount { get; private set; }

    public Task<AgentUpdateCheckOutcome> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        CheckCallCount++;
        return Task.FromResult(CheckOutcome);
    }

    public Task<AgentUpdateOffer?> GetOfferForAsync(string? currentAgentVersion, CancellationToken ct = default) =>
        Task.FromResult(IsEnabled ? Offer : null);

    public Task<AgentUpdateStatusDto> GetStatusAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<string?> ResolveDownloadPathAsync(string fileName, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
