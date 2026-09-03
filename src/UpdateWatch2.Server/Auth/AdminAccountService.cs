using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Auth;

public class AdminAccountService(AppDbContext db, ILogger<AdminAccountService> logger) : IAdminAccountService
{
    public const string DefaultUsername = "admin";

    private readonly PasswordHasher<AdminAccount> _hasher = new();

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        if (await db.AdminAccounts.AnyAsync(ct))
        {
            return;
        }

        var password = PasswordPolicy.Generate();
        var account = new AdminAccount { Username = DefaultUsername, PasswordHash = "" };
        account.PasswordHash = _hasher.HashPassword(account, password);

        db.AdminAccounts.Add(account);
        await db.SaveChangesAsync(ct);

        // Intentionally the only place this ever gets logged — see
        // CLAUDE.md: printed once at creation, then only retrievable by
        // changing it via the UI.
        logger.LogWarning(
            "Generated initial admin password (username '{Username}'): {Password} — change this after first login.",
            DefaultUsername, password);
    }

    public async Task<bool> VerifyPasswordAsync(string username, string password, CancellationToken ct = default)
    {
        var account = await db.AdminAccounts.SingleOrDefaultAsync(a => a.Username == username, ct);
        if (account is null)
        {
            return false;
        }

        var result = _hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    public async Task<bool> ChangePasswordAsync(string username, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        if (!PasswordPolicy.IsValid(newPassword))
        {
            return false;
        }

        var account = await db.AdminAccounts.SingleOrDefaultAsync(a => a.Username == username, ct);
        if (account is null)
        {
            return false;
        }

        var verification = _hasher.VerifyHashedPassword(account, account.PasswordHash, currentPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            return false;
        }

        account.PasswordHash = _hasher.HashPassword(account, newPassword);
        account.PasswordChangedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
