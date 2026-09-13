using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Auth;

public class AdminAccountService(
    AppDbContext db,
    IAuditLogService auditLog,
    ISessionInvalidationService sessionInvalidation,
    ILogger<AdminAccountService> logger) : IAdminAccountService
{
    public const string DefaultUsername = "admin";

    private const string ResetPasswordEnvVarName = "UPDATEWATCH2_RESET_ADMIN_PASSWORD";

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

    public async Task ResetPasswordFromEnvironmentIfConfiguredAsync(CancellationToken ct = default)
    {
        var newPassword = Environment.GetEnvironmentVariable(ResetPasswordEnvVarName);
        if (string.IsNullOrEmpty(newPassword))
        {
            return;
        }

        if (!PasswordPolicy.IsValid(newPassword))
        {
            // Deliberately doesn't quote the value itself — it's still a
            // secret even though it failed validation, no reason to put it
            // in a log line just because it didn't qualify.
            logger.LogError(
                "{EnvVar} is set but doesn't meet the password policy (at least {MinLength} characters, upper/lower/digit/symbol) — the admin password was NOT changed.",
                ResetPasswordEnvVarName, PasswordPolicy.MinLength);
            return;
        }

        var account = await db.AdminAccounts.SingleOrDefaultAsync(a => a.Username == DefaultUsername, ct);
        if (account is null)
        {
            // EnsureSeededAsync always runs first at the one real call site
            // (Program.cs) and guarantees this — only reachable here if
            // called out of order (e.g. directly from a test).
            return;
        }

        var newValueHash = HashEnvValue(newPassword);
        if (account.PasswordResetEnvValueHash == newValueHash)
        {
            // Same value as last time this fired — already applied,
            // nothing to do. Without this check, simply forgetting to
            // unset the variable would silently re-clobber the password on
            // every future restart, undoing any change the admin made
            // through the UI in between.
            return;
        }

        account.PasswordHash = _hasher.HashPassword(account, newPassword);
        account.PasswordResetEnvValueHash = newValueHash;
        account.PasswordChangedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // Security review finding: a password reset/change used to leave
        // any already-issued cookie ticket working — exactly the scenario
        // (a locked-out or compromised admin) this recovery mechanism
        // exists for should also kill off whatever session an attacker
        // might already be holding. See Db.Entities.SessionInvalidation's
        // doc comment.
        await sessionInvalidation.InvalidateAsync(account.Username, ct);

        logger.LogWarning(
            "Admin password (username '{Username}') was reset via {EnvVar} — remove or change that environment variable once you've logged in, or this will keep overriding future password changes.",
            DefaultUsername, ResetPasswordEnvVarName);
        await auditLog.LogAsync("environment", "admin.password.reset-via-environment", ct: ct);
    }

    private static string HashEnvValue(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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

        // Invalidates every already-issued cookie ticket for this account,
        // including the one making this very request — the admin will need
        // to log in again with the new password, the same expectation most
        // "change my password" flows set. See
        // Db.Entities.SessionInvalidation's doc comment for the finding
        // this closes (a password change used to leave existing sessions,
        // including a stolen one, working unaffected).
        await sessionInvalidation.InvalidateAsync(account.Username, ct);
        return true;
    }
}
