namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// The single local administrator account (CLAUDE.md section on the local
/// `admin` account). AD-authenticated users (updatewatch2-server#2) aren't
/// represented here — that's a separate, not-yet-implemented login path.
/// </summary>
public class AdminAccount
{
    public int Id { get; set; }

    public required string Username { get; set; }

    /// <summary>PBKDF2 hash produced by <see cref="Microsoft.AspNetCore.Identity.PasswordHasher{TUser}"/>, never the plaintext password.</summary>
    public required string PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? PasswordChangedAt { get; set; }

    /// <summary>
    /// SHA-256 hex digest of the raw <c>UPDATEWATCH2_RESET_ADMIN_PASSWORD</c>
    /// value this account's password was last reset from — see
    /// <see cref="Auth.AdminAccountService.ResetPasswordFromEnvironmentIfConfiguredAsync"/>.
    /// A content fingerprint only (never the password itself, and never
    /// used for authentication), purely so a stale, forgotten-and-left-set
    /// env var doesn't silently re-apply the SAME reset on every future
    /// restart and clobber a password the admin has since changed through
    /// the UI — only an actually-*different* env var value ever resets
    /// again. Null until the first time this mechanism has ever fired.
    /// </summary>
    public string? PasswordResetEnvValueHash { get; set; }
}
