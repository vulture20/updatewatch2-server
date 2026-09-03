using UpdateWatch2.Server.Auth;

namespace UpdateWatch2.Server.Tests.Auth;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Short1!", false)] // too short
    [InlineData("alllowercase1234!", false)] // no uppercase
    [InlineData("ALLUPPERCASE1234!", false)] // no lowercase
    [InlineData("NoDigitsHereAtAll!!", false)] // no digit
    [InlineData("NoSymbolsHere12345", false)] // no symbol
    [InlineData("Valid$Password1234", true)]
    public void IsValid_enforces_the_complexity_rule(string password, bool expected)
    {
        Assert.Equal(expected, PasswordPolicy.IsValid(password));
    }

    [Fact]
    public void Generate_produces_a_password_that_satisfies_IsValid()
    {
        for (var i = 0; i < 20; i++)
        {
            var password = PasswordPolicy.Generate();
            Assert.True(PasswordPolicy.IsValid(password), $"Generated password '{password}' failed validation.");
        }
    }

    [Fact]
    public void Generate_respects_a_longer_requested_length()
    {
        var password = PasswordPolicy.Generate(24);

        Assert.Equal(24, password.Length);
    }

    [Fact]
    public void Generate_never_produces_shorter_than_the_minimum_even_if_asked()
    {
        var password = PasswordPolicy.Generate(4);

        Assert.Equal(PasswordPolicy.MinLength, password.Length);
    }
}
