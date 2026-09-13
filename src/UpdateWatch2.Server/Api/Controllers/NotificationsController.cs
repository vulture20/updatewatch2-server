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
            return BadRequest(new { errors = new[] { new ApiErrorItem(ApiErrorCode.TestEmailToAddressInvalid, "ToAddress must be a valid email address.") } });
        }

        if (!settingsStore.Smtp.IsConfigured)
        {
            return BadRequest(new { errors = new[] { new ApiErrorItem(ApiErrorCode.SmtpNotConfigured, "SMTP is not configured.") } });
        }

        try
        {
            await emailService.SendTestEmailAsync(request.ToAddress, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Message stays the exact, unwrapped exception text (bad host,
            // auth failure, connection refused, ...) as the fallback for a
            // frontend build that doesn't recognize the code — Detail
            // carries the same text again, additively, purely so the
            // translated template (updatewatch2-server#17) has something
            // to interpolate; see ApiErrorCode.TestEmailFailed's own doc
            // comment for why this is one of only two codes that need it.
            return StatusCode(StatusCodes.Status502BadGateway, new { errors = new[] { new ApiErrorItem(ApiErrorCode.TestEmailFailed, ex.Message, ex.Message) } });
        }

        await auditLog.LogAsync(User.Identity!.Name!, "notifications.test-email.sent", request.ToAddress, ct);
        return NoContent();
    }
}
