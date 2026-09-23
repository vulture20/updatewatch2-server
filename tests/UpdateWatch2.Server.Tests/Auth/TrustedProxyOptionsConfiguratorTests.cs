using System.Net;
using Microsoft.AspNetCore.Builder;
using UpdateWatch2.Server.Auth;

namespace UpdateWatch2.Server.Tests.Auth;

public class TrustedProxyOptionsConfiguratorTests
{
    // ForwardedHeadersOptions' own constructor pre-populates KnownProxies
    // with the IPv6 loopback address by default — Program.cs's real call
    // site always Clear()s both lists immediately before ever calling
    // TryApply, so these tests match that real usage rather than the
    // constructor's own out-of-the-box defaults.
    private static ForwardedHeadersOptions CreateClearedOptions()
    {
        var options = new ForwardedHeadersOptions();
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        return options;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryApply_is_a_no_op_for_a_blank_value(string? value)
    {
        var options = CreateClearedOptions();

        var result = TrustedProxyOptionsConfigurator.TryApply(value, options, out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void TryApply_adds_a_single_bare_IP_to_KnownProxies()
    {
        var options = CreateClearedOptions();

        var result = TrustedProxyOptionsConfigurator.TryApply("10.0.5.4", options, out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal([IPAddress.Parse("10.0.5.4")], options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void TryApply_adds_a_single_CIDR_range_to_KnownIPNetworks()
    {
        var options = CreateClearedOptions();

        var result = TrustedProxyOptionsConfigurator.TryApply("172.20.0.0/16", options, out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Empty(options.KnownProxies);
        var network = Assert.Single(options.KnownIPNetworks);
        Assert.Equal(IPAddress.Parse("172.20.0.0"), network.BaseAddress);
        Assert.Equal(16, network.PrefixLength);
    }

    [Fact]
    public void TryApply_routes_a_comma_separated_mix_of_IPs_and_CIDR_ranges_to_the_right_lists()
    {
        var options = CreateClearedOptions();

        var result = TrustedProxyOptionsConfigurator.TryApply("10.0.5.4, 172.20.0.0/16, 10.0.5.5", options, out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal([IPAddress.Parse("10.0.5.4"), IPAddress.Parse("10.0.5.5")], options.KnownProxies);
        Assert.Single(options.KnownIPNetworks);
    }

    [Fact]
    public void TryApply_supports_IPv6_addresses_and_ranges()
    {
        var options = CreateClearedOptions();

        var result = TrustedProxyOptionsConfigurator.TryApply("::1, fd00::/8", options, out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal([IPAddress.Parse("::1")], options.KnownProxies);
        Assert.Single(options.KnownIPNetworks);
    }

    [Fact]
    public void TryApply_tolerates_surrounding_whitespace_around_entries()
    {
        var options = CreateClearedOptions();

        var result = TrustedProxyOptionsConfigurator.TryApply("  10.0.5.4  ,  172.20.0.0/16  ", options, out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Single(options.KnownProxies);
        Assert.Single(options.KnownIPNetworks);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("10.0.5.4,not-an-ip")]
    [InlineData("999.999.999.999")]
    [InlineData("10.0.0.0/999")]
    [InlineData("10.0.0.0/8,garbage/16")]
    public void TryApply_rejects_the_whole_value_when_any_entry_is_malformed(string value)
    {
        var options = CreateClearedOptions();

        var result = TrustedProxyOptionsConfigurator.TryApply(value, options, out var error);

        // Atomic validation: even a value with one valid entry alongside a
        // bad one must leave BOTH lists empty, not silently apply the
        // valid entry and drop only the bad one — a partial allow-list
        // would be a harder, more confusing failure for an admin to notice
        // than the feature clearly not being active at all.
        Assert.False(result);
        Assert.NotNull(error);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }
}
