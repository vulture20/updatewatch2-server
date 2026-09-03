using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace UpdateWatch2.Server.Notifications;

/// <summary>
/// Initial implementation using the built-in <see cref="SmtpClient"/>. It
/// only distinguishes "no encryption" from "encrypted" (STARTTLS-style
/// upgrade) — implicit TLS on port 465 isn't well supported by
/// <see cref="SmtpClient"/>. Revisit with MailKit if that matters.
/// </summary>
public class EmailNotificationService(IOptionsMonitor<SmtpOptions> options, ILogger<EmailNotificationService> logger)
    : IEmailNotificationService
{
    public async Task SendTestEmailAsync(string toAddress, CancellationToken ct = default)
    {
        var opts = options.CurrentValue;
        if (!opts.IsConfigured)
        {
            throw new InvalidOperationException("SMTP is not configured.");
        }

        using var client = BuildClient(opts);
        using var message = new MailMessage
        {
            From = new MailAddress(opts.FromAddress, opts.FromName),
            Subject = "UpdateWatch2 test email",
            Body = "This is a test email from UpdateWatch2. If you received this, SMTP is configured correctly.",
        };
        message.To.Add(toAddress);

        await client.SendMailAsync(message, ct);
        logger.LogInformation("Sent test email to {ToAddress}", toAddress);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        var opts = options.CurrentValue;
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
