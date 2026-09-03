namespace UpdateWatch2.Server.Auth;

/// <summary>
/// Admin-configurable brute-force login protection. Defaults match
/// CLAUDE.md: 6 failed attempts within 5 minutes locks out for 30 minutes.
/// </summary>
public class BruteForceOptions
{
    public const string SectionName = "BruteForce";

    public int MaxAttempts { get; set; } = 6;

    public int WindowMinutes { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 30;

    /// <summary>
    /// CIDR range (e.g. "10.0.0.0/8") exempt from brute-force lockout.
    /// Populated from the UPDATEWATCH2_TRUSTEDIP environment variable —
    /// see Program.cs — not from appsettings, per CLAUDE.md.
    /// </summary>
    public string? TrustedIpRange { get; set; }
}
