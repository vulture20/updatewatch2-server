namespace UpdateWatch2.Server.Notifications;

public interface IEmailNotificationService
{
    /// <summary>Sends a test email to verify the configured mail server, for the Administration test-mail button.</summary>
    Task SendTestEmailAsync(string toAddress, CancellationToken ct = default);

    /// <summary>
    /// True if the mail server is configured and currently reachable. Backs
    /// the red login-page warning banner (CLAUDE.md section 6.3) when false.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}
