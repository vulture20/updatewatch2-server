using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Wires up <see cref="IEmailNotificationService.SendTestEmailAsync"/> to
/// an actual HTTP endpoint — found missing by a user report ("es fehlt ein
/// Test-Mechanismus für den Mailversand"). That method (and
/// <see cref="IEmailNotificationService.IsHealthyAsync"/>, still unwired)
/// existed already, with a doc comment claiming it backed "the
/// Administration test-mail button", but no controller ever injected
/// <see cref="IEmailNotificationService"/> at all — the button/endpoint it
/// was written for was never actually built, so an admin had no way to
/// confirm SMTP host/port/credentials/encryption actually work end to end
/// before relying on any of it, including the new certificate-expiry
/// notifications (<see cref="Certificates.CertificateExpiryWorker"/>) this
/// same report followed directly from.
/// </summary>
[ApiController]
[Route("api/admin/notifications")]
[Authorize]
public class NotificationsController(IEmailNotificationService emailService, IAdminSettingsStore settingsStore, IAuditLogService auditLog) : ControllerBase
{
    public record TestEmailRequest(string ToAddress);

    [HttpPost("test-email")]
    public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ToAddress) || !request.ToAddress.Contains('@'))
        {
            return BadRequest(new { errors = new[] { "ToAddress must be a valid email address." } });
        }

        if (!settingsStore.Smtp.IsConfigured)
        {
            return BadRequest(new { errors = new[] { "SMTP is not configured." } });
        }

        try
        {
            await emailService.SendTestEmailAsync(request.ToAddress, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Surfaced verbatim to the admin UI — an SmtpException's own
            // message (bad host, auth failure, connection refused, ...) is
            // exactly what an admin debugging their SMTP config needs to
            // see, not a generic "something went wrong".
            return StatusCode(StatusCodes.Status502BadGateway, new { errors = new[] { ex.Message } });
        }

        await auditLog.LogAsync(User.Identity!.Name!, "notifications.test-email.sent", request.ToAddress, ct);
        return Ok();
    }
}
