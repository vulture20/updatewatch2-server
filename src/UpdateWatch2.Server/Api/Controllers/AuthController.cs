using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Auth;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Login for either the local `admin` account (CLAUDE.md section 3.3) or
/// an Active Directory user in the configured login group (section 6.1) —
/// two independent paths, tried in that order.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    IAdminAccountService accounts,
    IActiveDirectoryAuthService adAuth,
    IBruteForceLoginService bruteForce,
    IAuditLogService auditLog) : ControllerBase
{
    private const string AuthSourceClaimType = "updatewatch2:auth_source";

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;

        if (bruteForce.IsLockedOut(request.Username, remoteIp))
        {
            await auditLog.LogAsync(request.Username, "login.blocked", remoteIp?.ToString(), ct);
            return StatusCode(StatusCodes.Status423Locked, new { message = "Too many failed attempts. Try again later." });
        }

        string authSource;
        if (await accounts.VerifyPasswordAsync(request.Username, request.Password, ct))
        {
            authSource = "local";
        }
        else
        {
            var adResult = await adAuth.AuthenticateAsync(request.Username, request.Password, ct);
            if (!adResult.Success)
            {
                bruteForce.RecordFailedAttempt(request.Username, remoteIp);
                await auditLog.LogAsync(request.Username, "login.failed", remoteIp?.ToString(), ct);
                return Unauthorized(new { message = "Invalid username or password." });
            }

            authSource = "ad";
        }

        bruteForce.RecordSuccessfulLogin(request.Username, remoteIp);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, request.Username),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(AuthSourceClaimType, authSource),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        await auditLog.LogAsync(request.Username, $"login.success ({authSource})", remoteIp?.ToString(), ct);
        return Ok(new LoginResponse(request.Username));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var username = User.Identity?.Name ?? "unknown";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await auditLog.LogAsync(username, "logout", null, ct);
        return NoContent();
    }

    [HttpGet("me")]
    [AllowAnonymous]
    public IActionResult Me() =>
        Ok(new MeResponse(User.Identity?.IsAuthenticated ?? false, User.Identity?.Name));

    [HttpPut("password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var username = User.Identity!.Name!;

        // This changes the local `admin` account's password specifically —
        // an AD-authenticated session has no local account to change here
        // (that's the directory's own concern, out of scope for this app).
        if (User.HasClaim(AuthSourceClaimType, "ad"))
        {
            return BadRequest(new { message = "AD-authenticated sessions can't change the local admin password." });
        }

        var changed = await accounts.ChangePasswordAsync(username, request.CurrentPassword, request.NewPassword, ct);
        if (!changed)
        {
            return BadRequest(new { message = "Current password is incorrect, or the new password doesn't meet the complexity requirements." });
        }

        await auditLog.LogAsync(username, "admin.password.changed", null, ct);
        return NoContent();
    }
}
