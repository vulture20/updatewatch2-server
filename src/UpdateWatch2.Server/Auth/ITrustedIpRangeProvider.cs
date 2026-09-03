namespace UpdateWatch2.Server.Auth;

/// <summary>
/// The CIDR range exempt from brute-force lockout, per UPDATEWATCH2_TRUSTEDIP
/// — an operational/deployment concern (env var only), not an
/// admin-UI setting, per CLAUDE.md.
/// </summary>
public interface ITrustedIpRangeProvider
{
    string? TrustedIpRange { get; }
}

public class EnvironmentTrustedIpRangeProvider : ITrustedIpRangeProvider
{
    public string? TrustedIpRange => Environment.GetEnvironmentVariable("UPDATEWATCH2_TRUSTEDIP");
}
