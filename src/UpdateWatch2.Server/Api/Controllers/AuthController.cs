using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Auth;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Local admin-account login (CLAUDE.md section 3.3). AD-authenticated
/// login (updatewatch2-server#2) is a separate, not-yet-implemented path —
/// this controller only covers the local `admin` account.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    IAdminAccountService accounts,
    IBruteForceLoginService bruteForce,
    IAuditLogService auditLog) : ControllerBase
{
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

        if (!await accounts.VerifyPasswordAsync(request.Username, request.Password, ct))
        {
            bruteForce.RecordFailedAttempt(request.Username, remoteIp);
            await auditLog.LogAsync(request.Username, "login.failed", remoteIp?.ToString(), ct);
            return Unauthorized(new { message = "Invalid username or password." });
        }

        bruteForce.RecordSuccessfulLogin(request.Username, remoteIp);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, request.Username), new Claim(ClaimTypes.Role, "Admin")],
            CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        await auditLog.LogAsync(request.Username, "login.success", remoteIp?.ToString(), ct);
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
        var changed = await accounts.ChangePasswordAsync(username, request.CurrentPassword, request.NewPassword, ct);
        if (!changed)
        {
            return BadRequest(new { message = "Current password is incorrect, or the new password doesn't meet the complexity requirements." });
        }

        await auditLog.LogAsync(username, "admin.password.changed", null, ct);
        return NoContent();
    }
}
