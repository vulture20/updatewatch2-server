using System.Security.Cryptography;

namespace UpdateWatch2.Server.Certificates;

/// <summary>
/// Generates and verifies the opaque per-agent registration token used to
/// guard the certificate-onboarding flow (see <see cref="Db.Entities.Agent.RegistrationTokenHash"/>'s
/// doc comment for why it exists). The same secret-hygiene convention used
/// elsewhere in this codebase (passwords, AD bind credentials): only the
/// hash is ever persisted, never the raw token. Unlike a user password,
/// this token is high-entropy and random, not something a slow KDF needs to
/// protect against offline guessing — a fast, timing-safe comparison is what
/// matters here, the same spirit as <see cref="LdapFilterEscaper"/> being a
/// small, focused, unit-tested-on-its-own helper.
/// </summary>
public static class RegistrationTokenHasher
{
    private const int TokenSizeBytes = 32;

    /// <summary>Generates a new random token and its SHA-256 hash (hex-encoded, lowercase).</summary>
    public static (string RawToken, string Hash) GenerateToken()
    {
        var raw = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenSizeBytes));
        return (raw, Hash(raw));
    }

    /// <summary>Timing-safe comparison of a raw token against a previously stored hash.</summary>
    public static bool Verify(string rawToken, string storedHash)
    {
        var actualHash = Hash(rawToken);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(actualHash),
            System.Text.Encoding.UTF8.GetBytes(storedHash));
    }

    private static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
}
