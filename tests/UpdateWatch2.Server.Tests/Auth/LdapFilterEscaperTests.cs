using UpdateWatch2.Server.Auth;

namespace UpdateWatch2.Server.Tests.Auth;

public class LdapFilterEscaperTests
{
    [Theory]
    [InlineData("jdoe", "jdoe")]
    [InlineData("", "")]
    [InlineData("j.doe@example.com", "j.doe@example.com")]
    public void Leaves_ordinary_values_unchanged(string input, string expected)
    {
        Assert.Equal(expected, LdapFilterEscaper.Escape(input));
    }

    [Theory]
    // RFC 4515's five special characters, individually.
    [InlineData(@"\", @"\5c")]
    [InlineData("*", @"\2a")]
    [InlineData("(", @"\28")]
    [InlineData(")", @"\29")]
    [InlineData("\0", @"\00")]
    public void Escapes_each_special_character(string input, string expected)
    {
        Assert.Equal(expected, LdapFilterEscaper.Escape(input));
    }

    [Fact]
    public void Neutralizes_a_classic_ldap_filter_injection_attempt()
    {
        // Without escaping, this would turn "(&(objectClass=user)(sAMAccountName={0}))"
        // into a filter matching every user, bypassing the intended lookup.
        var malicious = "*)(objectClass=*";

        var escaped = LdapFilterEscaper.Escape(malicious);

        Assert.DoesNotContain('*', escaped);
        Assert.DoesNotContain('(', escaped);
        Assert.Equal(@"\2a\29\28objectClass=\2a", escaped);
    }
}
