namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// Records, per username (local <c>admin</c> account or an AD username —
/// this table has no foreign key to <see cref="AdminAccount"/>, since an
/// AD-authenticated session has no local account row to point at), the
/// timestamp before which any previously-issued cookie ticket must be
/// rejected. A username with no row here has never had its sessions
/// explicitly invalidated.
///
/// Security review finding: this app's cookie authentication was a pure,
/// stateless, Data-Protection-encrypted ticket with no server-side
/// revocation mechanism at all — <c>POST /api/auth/logout</c> only cleared
/// the *calling* browser's cookie (a stolen copy elsewhere kept working
/// indefinitely, and with <c>SlidingExpiration</c> a stolen ticket reused
/// periodically never truly expired), and changing the local admin
/// password didn't invalidate any ticket issued under the old password
/// either. Both <c>AuthController.Logout</c> and
/// <see cref="Auth.AdminAccountService.ChangePasswordAsync"/> (and its
/// sibling <see cref="Auth.AdminAccountService.ResetPasswordFromEnvironmentIfConfiguredAsync"/>)
/// now call <see cref="Auth.ISessionInvalidationService.InvalidateAsync"/>
/// on success, and <c>Program.cs</c>'s
/// <c>CookieAuthenticationEvents.OnValidatePrincipal</c> checks every
/// request's ticket-issued-at claim against this table, signing out (and
/// rejecting) anything issued before the recorded cutoff. This is a
/// deliberately simple per-username table, not a full session store keyed
/// by individual ticket — "invalidate everything for this account" is
/// exactly what both triggering actions (logout, password change) call
/// for, and it needs no cleanup job of its own since there's exactly one
/// row per username that has ever logged out or changed its password,
/// ever.
/// </summary>
public class SessionInvalidation
{
    public int Id { get; set; }

    public required string Username { get; set; }

    public DateTimeOffset InvalidatedBefore { get; set; }
}
