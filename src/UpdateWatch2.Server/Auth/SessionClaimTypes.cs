namespace UpdateWatch2.Server.Auth;

/// <summary>
/// Claim types embedded in the admin cookie ticket at sign-in
/// (<c>AuthController.Login</c>) beyond the standard <c>ClaimTypes.Name</c>/<c>Role</c>.
/// </summary>
public static class SessionClaimTypes
{
    /// <summary>
    /// When this ticket was issued (round-trip ISO 8601, UTC) — checked on
    /// every request against <see cref="ISessionInvalidationService"/> by
    /// <c>Program.cs</c>'s <c>CookieAuthenticationEvents.OnValidatePrincipal</c>,
    /// so a ticket issued before the account's most recent logout/password
    /// change is rejected even if it hasn't naturally expired yet.
    /// </summary>
    public const string IssuedAt = "updatewatch2:issued_at";
}
