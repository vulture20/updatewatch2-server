using System.Security.Cryptography;

namespace UpdateWatch2.Server.Auth;

/// <summary>
/// The complexity rule for the admin account's password (CLAUDE.md: "mindestens
/// 16 Zeichen lang, enthält Klein- und Großbuchstaben, Ziffern und
/// Sonderzeichen"). Applied both to the auto-generated initial password and
/// to any password an admin sets afterward via the change-password endpoint.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 16;

    private const string Lower = "abcdefghijkmnopqrstuvwxyz"; // no 'l' — avoid confusion with '1'/'I'
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // no 'I'/'O'
    private const string Digits = "23456789"; // no '0'/'1'
    private const string Symbols = "!@#$%^&*-_=+?";

    public static bool IsValid(string password) =>
        password.Length >= MinLength
        && password.Any(char.IsLower)
        && password.Any(char.IsUpper)
        && password.Any(char.IsDigit)
        && password.Any(c => Symbols.Contains(c));

    /// <summary>Generates a random password guaranteed to satisfy <see cref="IsValid"/>.</summary>
    public static string Generate(int length = MinLength)
    {
        if (length < MinLength)
        {
            length = MinLength;
        }

        var alphabet = Lower + Upper + Digits + Symbols;
        var chars = new char[length];

        // Guarantee at least one of each required character class, then fill
        // the rest randomly, then shuffle so the guaranteed characters
        // aren't always in the same positions.
        chars[0] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[1] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[3] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];

        for (var i = 4; i < length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
