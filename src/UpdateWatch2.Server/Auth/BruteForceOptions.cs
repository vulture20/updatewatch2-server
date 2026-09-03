namespace UpdateWatch2.Server.Auth;

/// <summary>
/// Admin-configurable brute-force login protection. Defaults match
/// CLAUDE.md: 6 failed attempts within 5 minutes locks out for 30 minutes.
/// Bound from appsettings.json's "BruteForce" section only as the
/// compiled-in default used to seed <see cref="UpdateWatch2.Server.Admin.AdminSettingsStore"/>
/// on first run — the database is authoritative after that. See
/// <see cref="ITrustedIpRangeProvider"/> for the (separate,
/// env-var-only) trusted-IP exemption.
/// </summary>
public class BruteForceOptions
{
    public const string SectionName = "BruteForce";

    public int MaxAttempts { get; set; } = 6;

    public int WindowMinutes { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 30;
}
