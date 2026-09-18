using UpdateWatch2.Server.AgentUpdates;

namespace UpdateWatch2.Server.Tests.AgentUpdates;

/// <summary>
/// Direct coverage of <see cref="AgentUpdateAssetClassifier.Classify"/> —
/// previously only exercised indirectly through
/// <see cref="AgentUpdateServiceTests"/>, worth testing on its own now that
/// it also has to tell two architectures of the same kind apart
/// (updatewatch2-agent#22/#23), not just classify by extension.
/// </summary>
public class AgentUpdateAssetClassifierTests
{
    [Theory]
    [InlineData("UpdateWatch2Agent-Setup-1.0.18-x64.exe", AgentUpdateAssetKind.WindowsInstaller, AgentUpdateAssetArch.X64)]
    [InlineData("UpdateWatch2Agent-Setup-1.0.18-arm64.exe", AgentUpdateAssetKind.WindowsInstaller, AgentUpdateAssetArch.Arm64)]
    [InlineData("updatewatch2-agent_1.0.19_amd64.deb", AgentUpdateAssetKind.LinuxDeb, AgentUpdateAssetArch.X64)]
    [InlineData("updatewatch2-agent_1.0.19_arm64.deb", AgentUpdateAssetKind.LinuxDeb, AgentUpdateAssetArch.Arm64)]
    [InlineData("updatewatch2-agent-1.0.19-1.x86_64.rpm", AgentUpdateAssetKind.LinuxRpm, AgentUpdateAssetArch.X64)]
    [InlineData("updatewatch2-agent-1.0.19-1.aarch64.rpm", AgentUpdateAssetKind.LinuxRpm, AgentUpdateAssetArch.Arm64)]
    public void Classify_identifies_every_real_kind_and_architecture_combination(string fileName, AgentUpdateAssetKind expectedKind, AgentUpdateAssetArch expectedArch)
    {
        var result = AgentUpdateAssetClassifier.Classify(fileName);

        Assert.NotNull(result);
        Assert.Equal(expectedKind, result!.Value.Kind);
        Assert.Equal(expectedArch, result.Value.Arch);
    }

    [Theory]
    [InlineData("checksums.txt")]
    [InlineData("UpdateWatch2Agent-Setup-1.0.18.exe")] // missing architecture suffix
    [InlineData("updatewatch2-agent_1.0.19.deb")] // missing architecture suffix
    [InlineData("updatewatch2-agent-1.0.19-1.rpm")] // missing architecture suffix
    [InlineData("updatewatch2-agent_1.0.19_i386.deb")] // an architecture this project has never published
    public void Classify_returns_null_for_anything_not_a_known_kind_and_architecture_combination(string fileName)
    {
        Assert.Null(AgentUpdateAssetClassifier.Classify(fileName));
    }
}
