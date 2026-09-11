using UpdateWatch2.Server.AgentUpdates;

namespace UpdateWatch2.Server.Tests.AgentUpdates;

public class AgentUpdateVersionExtractorTests
{
    [Theory]
    [InlineData("UpdateWatch2Agent-Setup-0.13.0-x64.exe", "0.13.0")]
    [InlineData("updatewatch2-agent_0.13.0_amd64.deb", "0.13.0")]
    [InlineData("updatewatch2-agent-0.13.0-1.x86_64.rpm", "0.13.0")]
    [InlineData("updatewatch2-agent_1.2.3_amd64.deb", "1.2.3")]
    public void Extract_finds_the_version_embedded_in_each_of_this_projects_real_filename_conventions(string fileName, string expected)
    {
        Assert.Equal(expected, AgentUpdateVersionExtractor.Extract(fileName));
    }

    [Theory]
    [InlineData("updatewatch2-agent_amd64.deb")]
    [InlineData("checksums.txt")]
    [InlineData("")]
    public void Extract_returns_null_when_no_x_y_z_version_is_present(string fileName)
    {
        Assert.Null(AgentUpdateVersionExtractor.Extract(fileName));
    }
}
