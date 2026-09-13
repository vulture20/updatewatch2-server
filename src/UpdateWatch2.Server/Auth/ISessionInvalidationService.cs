namespace UpdateWatch2.Server.Auth;

/// <summary>
/// Backs server-side revocation of previously-issued admin cookie tickets —
/// see <see cref="Db.Entities.SessionInvalidation"/>'s doc comment for the
/// security review finding this exists to fix.
/// </summary>
public interface ISessionInvalidationService
{
    /// <summary>
    /// Marks every cookie ticket for <paramref name="username"/> issued up
    /// to and including this moment as no longer valid. Called on logout
    /// and on a successful local-admin password change.
    /// </summary>
    Task InvalidateAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// True unless <paramref name="username"/> has an invalidation on
    /// record at or after <paramref name="issuedAtUtc"/> — the timestamp
    /// embedded in the cookie ticket's own claim at sign-in time.
    /// </summary>
    Task<bool> IsValidAsync(string username, DateTimeOffset issuedAtUtc, CancellationToken ct = default);
}
