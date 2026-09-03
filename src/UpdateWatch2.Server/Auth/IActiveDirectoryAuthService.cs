namespace UpdateWatch2.Server.Auth;

public record AdAuthResult(bool Success, string? DisplayName, string? FailureReason);

/// <summary>
/// Authenticates against Active Directory (or any LDAPv3 directory) — a
/// second, independent login path alongside the local `admin` account
/// (<see cref="IAdminAccountService"/>). See CLAUDE.md section 6.1.
/// </summary>
public interface IActiveDirectoryAuthService
{
    Task<AdAuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default);
}
