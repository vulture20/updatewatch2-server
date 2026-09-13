using UpdateWatch2.Server.Agents;

namespace UpdateWatch2.Server.Tests.Agents;

public class HostnameValidatorTests
{
    [Theory]
    [InlineData("hpn54l")]
    [InlineData("web-01")]
    [InlineData("host.example.com")]
    [InlineData("a")]
    [InlineData("demo-agent-1")]
    public void IsValid_accepts_plausible_hostnames_and_FQDNs(string hostname)
    {
        Assert.True(HostnameValidator.IsValid(hostname));
    }

    [Theory]
    [InlineData("")]
    [InlineData("evil,O=Injected,OU=FakeOrg")] // the actual DN-injection payload this validator exists to block
    [InlineData("evil,CN=admin")]
    [InlineData("host=value")]
    [InlineData("host/path")]
    [InlineData("host name")]
    [InlineData("-leading-hyphen")]
    [InlineData("trailing-hyphen-")]
    [InlineData("host\nname")]
    public void IsValid_rejects_anything_that_is_not_a_plausible_hostname(string hostname)
    {
        Assert.False(HostnameValidator.IsValid(hostname));
    }

    [Fact]
    public void IsValid_rejects_a_hostname_longer_than_253_characters()
    {
        var tooLong = string.Concat(Enumerable.Repeat("a", 254));
        Assert.False(HostnameValidator.IsValid(tooLong));
    }
}
