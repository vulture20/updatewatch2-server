namespace UpdateWatch2.Server.Agents;

/// <summary>
/// Caps the self-reported metadata fields (<see cref="AgentRegisterRequest.DnsName"/>/
/// <see cref="AgentRegisterRequest.OperatingSystem"/>/<see cref="AgentRegisterRequest.IpAddress"/>/
/// <see cref="AgentRegisterRequest.AgentVersion"/>) at generous but finite
/// lengths — found by a security review: these had no length limit
/// anywhere (not the DTO, not the <see cref="Db.Entities.Agent"/> column,
/// not a route-specific request-size cap), and <c>POST .../register</c>
/// is deliberately anonymous (an agent has no client certificate yet at
/// first contact, per <see cref="AgentRegistrationService"/>'s own doc
/// comment) — an unauthenticated network caller could register unboundedly
/// many distinct hostnames, each carrying up to Kestrel's default
/// ~28.6 MB request body per text field, growing the database and the
/// admin's pending-approval queue with nothing to cap it.
/// <see cref="HostnameValidator"/> already bounds <c>Hostname</c> itself
/// (253 chars, RFC 1123) — this covers the four sibling fields that had
/// nothing at all.
///
/// Real-world values are always far smaller than these limits — generous
/// enough for any legitimate DNS name/OS description/IPv6 literal/version
/// string, small enough that a spam-registration burst can no longer
/// meaningfully grow the database per row.
/// </summary>
public static class AgentMetadataValidator
{
    public const int MaxDnsNameLength = 253;
    public const int MaxOperatingSystemLength = 256;
    public const int MaxIpAddressLength = 45; // longest valid IPv6 literal, e.g. with a zone ID
    public const int MaxAgentVersionLength = 32;

    /// <summary>
    /// Used at registration (<see cref="AgentRegistrationService.RegisterAsync"/>) —
    /// an oversized field there rejects the whole call outright, the same
    /// treatment <see cref="HostnameValidator"/> already gets, since this
    /// is the one anonymous, unauthenticated entry point.
    /// </summary>
    public static bool IsValid(AgentRegisterRequest request) =>
        IsWithinLength(request.DnsName, MaxDnsNameLength)
        && IsWithinLength(request.OperatingSystem, MaxOperatingSystemLength)
        && IsWithinLength(request.IpAddress, MaxIpAddressLength)
        && IsWithinLength(request.AgentVersion, MaxAgentVersionLength);

    /// <summary>
    /// Used on the heartbeat (<see cref="AgentRegistrationService.RecordAliveAsync"/>) —
    /// truncating rather than rejecting the whole heartbeat, since that
    /// path requires an already-approved, mTLS-authenticated agent (a much
    /// smaller, already-trusted population than anonymous registration),
    /// and this is purely display metadata, not security-relevant.
    /// </summary>
    public static string? Clamp(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static bool IsWithinLength(string? value, int maxLength) => value is null || value.Length <= maxLength;
}
