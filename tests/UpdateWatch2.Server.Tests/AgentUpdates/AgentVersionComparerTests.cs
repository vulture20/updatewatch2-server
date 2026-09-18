using UpdateWatch2.Server.AgentUpdates;

namespace UpdateWatch2.Server.Tests.AgentUpdates;

public class AgentVersionComparerTests
{
    [Theory]
    [InlineData("1.0.16", "1.0.20", true)]
    [InlineData("1.0.20", "1.0.20", false)]
    [InlineData("1.0.21", "1.0.20", false)]
    [InlineData("0.9.9", "1.0.0", true)]
    public void IsOlderThan_compares_parsed_SemVer_style_versions(string version, string referenceVersion, bool expected)
    {
        Assert.Equal(expected, AgentVersionComparer.IsOlderThan(version, referenceVersion));
    }

    [Theory]
    [InlineData(null, "1.0.20")]
    [InlineData("", "1.0.20")]
    [InlineData("1.0.16", null)]
    [InlineData("1.0.16", "")]
    [InlineData("not-a-version", "1.0.20")]
    [InlineData("1.0.16", "not-a-version")]
    public void IsOlderThan_errs_toward_false_when_either_version_is_missing_or_unparsable(string? version, string? referenceVersion)
    {
        Assert.False(AgentVersionComparer.IsOlderThan(version, referenceVersion));
    }
}
