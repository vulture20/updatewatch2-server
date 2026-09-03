using System.Net;
using UpdateWatch2.Server.Auth;

namespace UpdateWatch2.Server.Tests.Auth;

public class TrustedIpMatcherTests
{
    [Theory]
    [InlineData("10.0.0.0/8", "10.1.2.3", true)]
    [InlineData("10.0.0.0/8", "11.1.2.3", false)]
    [InlineData("192.168.1.0/24", "192.168.1.255", true)]
    [InlineData("192.168.1.0/24", "192.168.2.1", false)]
    [InlineData("192.168.1.5/32", "192.168.1.5", true)]
    [InlineData("192.168.1.5/32", "192.168.1.6", false)]
    public void IsTrusted_matches_cidr_ranges(string cidr, string address, bool expected)
    {
        var result = TrustedIpMatcher.IsTrusted(cidr, IPAddress.Parse(address));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsTrusted_returns_false_when_range_not_configured()
    {
        Assert.False(TrustedIpMatcher.IsTrusted(null, IPAddress.Parse("10.0.0.1")));
        Assert.False(TrustedIpMatcher.IsTrusted("", IPAddress.Parse("10.0.0.1")));
    }

    [Fact]
    public void IsTrusted_returns_false_when_remote_ip_missing()
    {
        Assert.False(TrustedIpMatcher.IsTrusted("10.0.0.0/8", null));
    }
}
