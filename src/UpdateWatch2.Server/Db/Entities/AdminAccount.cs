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
}
