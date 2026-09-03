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

    Task<bool> VerifyPasswordAsync(string username, string password, CancellationToken ct = default);

    /// <summary>Returns false if <paramref name="currentPassword"/> doesn't match or <paramref name="newPassword"/> fails <see cref="PasswordPolicy"/>.</summary>
    Task<bool> ChangePasswordAsync(string username, string currentPassword, string newPassword, CancellationToken ct = default);
}
