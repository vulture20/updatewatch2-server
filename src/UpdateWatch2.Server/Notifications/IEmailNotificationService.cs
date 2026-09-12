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

    /// <summary>
    /// Sends a bilingual (English + German) automated notification — the
    /// first real one this project sends, used today by
    /// <see cref="Certificates.CertificateExpiryWorker"/> for the
    /// CA-root/server-certificate expiry warning and renewal notice, and
    /// by <see cref="UpdateThresholdNotificationWorker"/> for the
    /// update-notification thresholds. Unlike <see cref="SendTestEmailAsync"/>,
    /// subject/body are caller-supplied rather than hardcoded — this
    /// method has no opinion about what it's announcing.
    /// <paramref name="subjectEn"/> is also the mail's actual Subject
    /// header (there's no per-recipient language preference to pick
    /// between for a single configured mailbox); <paramref name="subjectDe"/>
    /// is only ever shown as that language section's own heading inside
    /// the email body — see <see cref="EmailTemplate"/>. Throws
    /// <see cref="InvalidOperationException"/> if SMTP isn't configured,
    /// same as <see cref="SendTestEmailAsync"/> — callers that can't
    /// guarantee that's already true should check
    /// <see cref="Admin.IAdminSettingsStore"/>'s live <c>Smtp.IsConfigured</c>
    /// first rather than relying on this to fail gracefully.
    /// </summary>
    Task SendNotificationAsync(string toAddress, string subjectEn, string bodyEn, string subjectDe, string bodyDe, CancellationToken ct = default);
}
