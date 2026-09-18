using System.Text.Json;
using UpdateWatch2.Server.AgentUpdates;

namespace UpdateWatch2.Server.Tests.AgentUpdates;

/// <summary>
/// Regression coverage for a real, user-reported production bug
/// (updatewatch2-server#24): when the six-slot, architecture-aware
/// <see cref="AgentUpdateOffer"/> shape replaced the original three-slot
/// one outright (server v1.3.24) instead of adding the new fields
/// alongside the old ones, every agent build below v1.0.20 — which only
/// knows the three old field names — could no longer read any asset out
/// of the response at all, breaking self-update for the entire
/// already-deployed fleet with no automatic way to recover (self-update
/// being the only delivery mechanism for the very fix that would un-stick
/// it). Nothing in this codebase's test suite caught that the first time,
/// since every existing test either asserted on <see cref="AgentUpdateOffer"/>'s
/// own (already-renamed) properties directly, or round-tripped through the
/// same, single, always-in-sync C# type on both ends — never through an
/// actually-old wire shape the way a real pre-v1.0.20 agent would. This
/// class does that for real: <see cref="OldAgentUpdateOffer"/> below is a
/// deliberately hand-written stand-in for that old agent-side type,
/// containing ONLY the three original field names, and the tests
/// deserialize a genuine, freshly serialized server response into it.
/// </summary>
public class AgentUpdateOfferBackwardCompatibilityTests
{
    private static readonly AgentUpdateAssetOffer WindowsX64Asset = new("/api/agent/updates/setup-x64.exe", "sha-win-x64", 1000);
    private static readonly AgentUpdateAssetOffer DebX64Asset = new("/api/agent/updates/pkg-amd64.deb", "sha-deb-x64", 2000);
    private static readonly AgentUpdateAssetOffer RpmX64Asset = new("/api/agent/updates/pkg-x86_64.rpm", "sha-rpm-x64", 3000);

    /// <summary>
    /// Stands in for a pre-v1.0.20 agent's own <c>AgentUpdateOffer</c> —
    /// deliberately kept in sync BY HAND with what that old type actually
    /// looked like (three fields, no architecture split), never with the
    /// current server-side type, since the whole point is to prove the
    /// server's wire output still satisfies a consumer that can never be
    /// updated to know about the new fields.
    /// </summary>
    private record OldAgentUpdateOffer(
        string Version,
        AgentUpdateAssetOffer? WindowsInstaller,
        AgentUpdateAssetOffer? LinuxDeb,
        AgentUpdateAssetOffer? LinuxRpm);

    [Fact]
    public void A_pre_v1_0_20_agent_can_still_read_a_real_asset_from_the_current_wire_shape()
    {
        var offer = new AgentUpdateOffer(
            "1.0.21",
            WindowsInstallerX64: WindowsX64Asset, WindowsInstallerArm64: null,
            LinuxDebX64: DebX64Asset, LinuxDebArm64: null,
            LinuxRpmX64: RpmX64Asset, LinuxRpmArm64: null);

        var json = JsonSerializer.Serialize(offer, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var oldShapeOffer = JsonSerializer.Deserialize<OldAgentUpdateOffer>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(oldShapeOffer);
        Assert.Equal("1.0.21", oldShapeOffer!.Version);
        Assert.NotNull(oldShapeOffer.WindowsInstaller);
        Assert.Equal("/api/agent/updates/setup-x64.exe", oldShapeOffer.WindowsInstaller!.DownloadUrl);
        Assert.NotNull(oldShapeOffer.LinuxDeb);
        Assert.Equal("/api/agent/updates/pkg-amd64.deb", oldShapeOffer.LinuxDeb!.DownloadUrl);
        Assert.NotNull(oldShapeOffer.LinuxRpm);
        Assert.Equal("/api/agent/updates/pkg-x86_64.rpm", oldShapeOffer.LinuxRpm!.DownloadUrl);
    }

    [Fact]
    public void The_old_field_names_always_alias_the_X64_slot_never_the_Arm64_one()
    {
        var arm64Only = new AgentUpdateOffer(
            "1.0.21",
            WindowsInstallerX64: null, WindowsInstallerArm64: new("/api/agent/updates/setup-arm64.exe", "sha", 1),
            LinuxDebX64: null, LinuxDebArm64: new("/api/agent/updates/pkg-arm64.deb", "sha", 1),
            LinuxRpmX64: null, LinuxRpmArm64: new("/api/agent/updates/pkg-aarch64.rpm", "sha", 1));

        // A pre-v1.0.20 agent (necessarily x64 — arm64 packages didn't
        // exist before this feature) must never be handed the arm64 asset
        // through the old field names, even when that's the only asset a
        // release happens to carry — the correct outcome is "nothing for
        // this old agent to do yet", exactly like a genuinely asset-less
        // release, not a wrong-architecture download.
        Assert.Null(arm64Only.WindowsInstaller);
        Assert.Null(arm64Only.LinuxDeb);
        Assert.Null(arm64Only.LinuxRpm);
    }

    [Fact]
    public void The_old_field_names_are_present_in_the_literal_serialized_JSON()
    {
        var offer = new AgentUpdateOffer(
            "1.0.21",
            WindowsInstallerX64: WindowsX64Asset, WindowsInstallerArm64: null,
            LinuxDebX64: DebX64Asset, LinuxDebArm64: null,
            LinuxRpmX64: RpmX64Asset, LinuxRpmArm64: null);

        var json = JsonSerializer.Serialize(offer, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"windowsInstaller\":", json);
        Assert.Contains("\"linuxDeb\":", json);
        Assert.Contains("\"linuxRpm\":", json);
        // ...alongside the new fields, not instead of them — a v1.0.20+
        // agent still needs these for correct architecture selection.
        Assert.Contains("\"windowsInstallerX64\":", json);
        Assert.Contains("\"windowsInstallerArm64\":", json);
        Assert.Contains("\"linuxDebX64\":", json);
        Assert.Contains("\"linuxDebArm64\":", json);
        Assert.Contains("\"linuxRpmX64\":", json);
        Assert.Contains("\"linuxRpmArm64\":", json);
    }
}
