namespace UpdateWatch2.Server.Certificates;

/// <summary>One rejected-certificate attempt, for the admin-facing recent list.</summary>
public record CertificateRejectionDto(DateTimeOffset Timestamp, string Reason, string? Details);

/// <summary>
/// Backs the admin UI's warning banner (high-priority/security per
/// CLAUDE.md) — <see cref="RecentCount"/> is the true total within the
/// lookback window even when <see cref="Recent"/> itself is capped (same
/// "count is always the true total, the list may be shorter" convention
/// <c>CaRotationImpactDto</c> already established).
/// </summary>
public record CertificateRejectionStatusDto(int RecentCount, IReadOnlyList<CertificateRejectionDto> Recent);
