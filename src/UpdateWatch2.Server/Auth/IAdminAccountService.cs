namespace UpdateWatch2.Server.Auth;

public interface IAdminAccountService
{
    /// <summary>
    /// Creates the local admin account with a random password on first run
    /// if none exists yet, logging the generated password once (CLAUDE.md:
    /// "wird bei der Erzeugung im Log ausgegeben"). Safe to call on every
    /// startup — a no-op once the account exists.
    /// </summary>
    Task EnsureSeededAsync(CancellationToken ct = default);

    /// <summary>
    /// Emergency recovery for a locked-out admin, at the user's explicit
    /// request ("Schaffe eine Möglichkeit das Admin-Passwort
    /// zurückzusetzen (überschreiben durch Umgebungsvariable?)"): if
    /// <c>UPDATEWATCH2_RESET_ADMIN_PASSWORD</c> is set to a
    /// <see cref="PasswordPolicy"/>-valid value that hasn't already been
    /// applied, overwrites the local admin account's password with it —
    /// regardless of the current password, unlike <see cref="ChangePasswordAsync"/>.
    /// Safe to call on every startup, right after <see cref="EnsureSeededAsync"/>:
    /// a no-op when the variable is unset, and (see
    /// <see cref="Db.Entities.AdminAccount.PasswordResetEnvValueHash"/>)
    /// a no-op on every subsequent restart too, as long as the variable's
    /// value hasn't changed since the last time it fired — a stale,
    /// forgotten env var left in place doesn't keep clobbering a password
    /// the admin has since changed through the UI. An invalid value is
    /// logged as an error and never applied.
    /// </summary>
    Task ResetPasswordFromEnvironmentIfConfiguredAsync(CancellationToken ct = default);

    Task<bool> VerifyPasswordAsync(string username, string password, CancellationToken ct = default);

    /// <summary>Returns false if <paramref name="currentPassword"/> doesn't match or <paramref name="newPassword"/> fails <see cref="PasswordPolicy"/>.</summary>
    Task<bool> ChangePasswordAsync(string username, string currentPassword, string newPassword, CancellationToken ct = default);
}
