using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Net.Sockets;
using System.Text;
using UpdateWatch2.Server.Admin;

namespace UpdateWatch2.Server.Notifications;

/// <summary>
/// Initial implementation using the built-in <see cref="SmtpClient"/>. It
/// only distinguishes "no encryption" from "encrypted" (STARTTLS-style
/// upgrade) — implicit TLS on port 465 isn't well supported by
/// <see cref="SmtpClient"/>. Revisit with MailKit if that matters.
///
/// Every message sent through here (both the test email and every real
/// automated notification) is now a proper, bilingual (English + German)
/// HTML email in the app's own "Nocturne" visual design, with the
/// UpdateWatch2 logo embedded inline and, when
/// <see cref="SmtpOptions.InstanceUrl"/> is configured, a link/button back
/// to this instance — all at the user's explicit request.
/// <see cref="BuildMessage"/> is the one place both send paths build the
/// actual <see cref="MailMessage"/> from, kept as a public static method
/// (rather than inlined into each send method) specifically so it can be
/// unit-tested without a real SMTP server — see <see cref="EmailTemplate"/>
/// for the HTML itself.
/// </summary>
public class EmailNotificationService(IAdminSettingsStore settingsStore, ILogger<EmailNotificationService> logger)
    : IEmailNotificationService
{
    // Loaded once per process from the assembly's own embedded resource
    // (UpdateWatch2.Server.csproj's <EmbeddedResource>) rather than a loose
    // file on disk — guarantees the logo is present regardless of the
    // container's working directory/content root, the same class of gap
    // Certs:Path/AgentUpdates:Path already document elsewhere for
    // path-relative config that DOES depend on where the process happens
    // to be running from.
    private static readonly Lazy<byte[]> LogoPngBytes = new(LoadLogoPngBytes);

    public async Task SendTestEmailAsync(string toAddress, CancellationToken ct = default)
    {
        var opts = settingsStore.Smtp;
        if (!opts.IsConfigured)
        {
            throw new InvalidOperationException("SMTP is not configured.");
        }

        using var client = BuildClient(opts);
        using var message = BuildMessage(
            opts.FromAddress,
            opts.FromName,
            toAddress,
            "UpdateWatch2 test email",
            "This is a test email from UpdateWatch2. If you received this, SMTP is configured correctly.",
            "UpdateWatch2-Test-E-Mail",
            "Dies ist eine Test-E-Mail von UpdateWatch2. Wenn du diese E-Mail erhalten hast, ist SMTP korrekt konfiguriert.",
            opts.InstanceUrl,
            LogoPngBytes.Value);

        await client.SendMailAsync(message, ct);
        logger.LogInformation("Sent test email to {ToAddress}", toAddress);
    }

    public async Task SendNotificationAsync(string toAddress, string subjectEn, string bodyEn, string subjectDe, string bodyDe, CancellationToken ct = default)
    {
        var opts = settingsStore.Smtp;
        if (!opts.IsConfigured)
        {
            throw new InvalidOperationException("SMTP is not configured.");
        }

        using var client = BuildClient(opts);
        using var message = BuildMessage(opts.FromAddress, opts.FromName, toAddress, subjectEn, bodyEn, subjectDe, bodyDe, opts.InstanceUrl, LogoPngBytes.Value);

        await client.SendMailAsync(message, ct);
        logger.LogInformation("Sent notification email to {ToAddress}: {Subject}", toAddress, subjectEn);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        var opts = settingsStore.Smtp;
        if (!opts.IsConfigured)
        {
            return false;
        }

        try
        {
            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(opts.Host, opts.Port, ct).AsTask();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), ct);
            var completed = await Task.WhenAny(connectTask, timeoutTask);
            return completed == connectTask && tcpClient.Connected;
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            logger.LogWarning(ex, "SMTP health check failed for {Host}:{Port}", opts.Host, opts.Port);
            return false;
        }
    }

    /// <summary>
    /// Builds the actual message both send paths use: a plain-text
    /// <see cref="MailMessage.Body"/> fallback (English then German,
    /// separated by a rule, plus the instance link if configured — for a
    /// client with no HTML support) plus an HTML <see cref="AlternateView"/>
    /// in the app's own bilingual design with the logo attached as a
    /// `cid:`-referenced <see cref="LinkedResource"/>, never a remote-hosted
    /// &lt;img&gt; — deliberately, so the logo always renders even for a
    /// recipient whose mail client blocks remote images by default (the
    /// common case for a first-time sender). <paramref name="subjectEn"/>
    /// is also the actual mail Subject header — see
    /// <see cref="IEmailNotificationService.SendNotificationAsync"/>'s doc
    /// comment for why. Public + static so tests can inspect the
    /// resulting <see cref="MailMessage"/> without a real SMTP server.
    /// </summary>
    public static MailMessage BuildMessage(
        string fromAddress, string fromName, string toAddress,
        string subjectEn, string bodyEn, string subjectDe, string bodyDe,
        string? instanceUrl, byte[] logoPngBytes)
    {
        var message = new MailMessage
        {
            From = new MailAddress(fromAddress, fromName),
            Subject = subjectEn,
            Body = BuildPlainTextBody(subjectEn, bodyEn, subjectDe, bodyDe, instanceUrl),
            IsBodyHtml = false,
        };
        message.To.Add(toAddress);

        var html = EmailTemplate.BuildHtml(StripSubjectPrefix(subjectEn), bodyEn, StripSubjectPrefix(subjectDe), bodyDe, instanceUrl);
        var htmlView = AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html");
        htmlView.LinkedResources.Add(new LinkedResource(new MemoryStream(logoPngBytes), "image/png")
        {
            ContentId = EmailTemplate.LogoContentId,
            TransferEncoding = TransferEncoding.Base64,
        });
        message.AlternateViews.Add(htmlView);

        return message;
    }

    private static string BuildPlainTextBody(string subjectEn, string bodyEn, string subjectDe, string bodyDe, string? instanceUrl)
    {
        var text = $"{StripSubjectPrefix(subjectEn)}\n\n{bodyEn}\n\n---\n\n{StripSubjectPrefix(subjectDe)}\n\n{bodyDe}";
        return string.IsNullOrWhiteSpace(instanceUrl) ? text : $"{text}\n\n{instanceUrl}";
    }

    /// <summary>
    /// Every existing notification subject in this codebase is prefixed
    /// "UpdateWatch2: " (e.g. "UpdateWatch2: CA root certificate expiring
    /// soon") — appropriate for a mail client's subject line/inbox list,
    /// but redundant once the email body itself already shows the
    /// UpdateWatch2 logo and wordmark right above the heading. Stripped
    /// here, once, rather than asking every caller to pass a separate
    /// "and also here's the heading without the prefix" value. A subject
    /// with no such prefix (the test email's own "UpdateWatch2 test
    /// email", which never had the colon) passes through unchanged.
    /// </summary>
    public static string StripSubjectPrefix(string subject) =>
        subject.StartsWith("UpdateWatch2: ", StringComparison.Ordinal) ? subject["UpdateWatch2: ".Length..] : subject;

    private static byte[] LoadLogoPngBytes()
    {
        var assembly = typeof(EmailNotificationService).Assembly;
        const string resourceName = "UpdateWatch2.Server.Resources.Email.logo.png";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static SmtpClient BuildClient(SmtpOptions opts)
    {
        var client = new SmtpClient(opts.Host, opts.Port)
        {
            EnableSsl = opts.Encryption is SmtpEncryption.SslTls or SmtpEncryption.StartTls,
        };

        if (!string.IsNullOrEmpty(opts.Username))
        {
            client.Credentials = new NetworkCredential(opts.Username, opts.Password);
        }

        return client;
    }
}
