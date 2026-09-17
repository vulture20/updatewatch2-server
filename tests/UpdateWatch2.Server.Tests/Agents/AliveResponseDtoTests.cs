using System.Text.Json;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.AgentUpdates;

namespace UpdateWatch2.Server.Tests.Agents;

/// <summary>
/// Regression coverage for a real bug (found by a user report — "Änderungen
/// werden aktuell nicht in die Registry geschrieben. Egal, was ausgewählt
/// oder eingetragen wird."): <c>AgentProtocolController.Alive</c> used to
/// build its response as an inline anonymous object that silently dropped
/// <see cref="AliveRecordResult.DesiredLogLevel"/> and its siblings —
/// nothing in this codebase's test suite could catch that, since
/// <c>WebApplicationFactory</c> can't exercise this mTLS-gated controller's
/// actual success path at all (see <c>UpdatesEndpointTests</c>' own doc
/// comment on that same limitation). The second test below — asserting on
/// the literal serialized JSON, not just the DTO's own properties — is the
/// one that would have caught the original bug; asserting only on
/// <see cref="AliveResponseDto"/>'s properties would not have, since the
/// bug was specifically in mapping into the wire shape, not in the DTO
/// itself.
/// </summary>
public class AliveResponseDtoTests
{
    [Fact]
    public void FromResult_maps_every_field_including_the_deliberate_UpdateAvailable_rename()
    {
        var offer = new AgentUpdateOffer("1.2.3", null, null, null);
        var result = new AliveRecordResult(
            InstallRequested: true, InstallUpdateIds: ["KB1"], UpdateAvailable: offer, CertificateRotationPending: true,
            RebootRequested: true, PreDownloadWindowsUpdatesEnabled: true, DesiredLogLevel: "DEBUG",
            DesiredUpdateCheckIntervalMinutes: 15, DesiredUpdateCheckJitterSeconds: 5, DesiredAliveIntervalMinutes: 10,
            PreDownloadLinuxUpdatesEnabled: true);

        var dto = AliveResponseDto.FromResult(result);

        Assert.True(dto.InstallRequested);
        Assert.Equal(["KB1"], dto.InstallUpdateIds);
        Assert.Same(offer, dto.AgentUpdateAvailable);
        Assert.True(dto.CertificateRotationPending);
        Assert.True(dto.RebootRequested);
        Assert.True(dto.PreDownloadWindowsUpdatesEnabled);
        Assert.Equal("DEBUG", dto.DesiredLogLevel);
        Assert.Equal(15, dto.DesiredUpdateCheckIntervalMinutes);
        Assert.Equal(5, dto.DesiredUpdateCheckJitterSeconds);
        Assert.Equal(10, dto.DesiredAliveIntervalMinutes);
        Assert.True(dto.PreDownloadLinuxUpdatesEnabled);
    }

    [Fact]
    public void FromResult_serializes_with_the_exact_JSON_property_names_the_agent_expects()
    {
        var result = new AliveRecordResult(
            InstallRequested: false, InstallUpdateIds: null, UpdateAvailable: null, CertificateRotationPending: false,
            RebootRequested: false, PreDownloadWindowsUpdatesEnabled: false, DesiredLogLevel: "DEBUG",
            DesiredUpdateCheckIntervalMinutes: 15, DesiredUpdateCheckJitterSeconds: 5, DesiredAliveIntervalMinutes: 10,
            PreDownloadLinuxUpdatesEnabled: true);
        var dto = AliveResponseDto.FromResult(result);

        // Matches ASP.NET Core's own default MVC JSON options
        // (JsonNamingPolicy.CamelCase, unconfigured/default in Program.cs's
        // plain AddControllers() call) — the same policy Ok(dto) actually
        // serializes with over the wire.
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"desiredLogLevel\":\"DEBUG\"", json);
        Assert.Contains("\"desiredUpdateCheckIntervalMinutes\":15", json);
        Assert.Contains("\"desiredUpdateCheckJitterSeconds\":5", json);
        Assert.Contains("\"desiredAliveIntervalMinutes\":10", json);
        Assert.Contains("\"preDownloadLinuxUpdatesEnabled\":true", json);
        Assert.Contains("\"agentUpdateAvailable\"", json);
        Assert.DoesNotContain("\"updateAvailable\"", json);
    }
}
