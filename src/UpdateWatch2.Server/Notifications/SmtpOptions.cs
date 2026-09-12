namespace UpdateWatch2.Server.Notifications;

public enum SmtpEncryption
{
    None,
    SslTls,
    StartTls,
}

/// <summary>
/// Central mail server configuration (Administration area, CLAUDE.md
/// section 6.3). Bound from appsettings.json's "Smtp" section only as the
/// compiled-in default used to seed <see cref="UpdateWatch2.Server.Admin.AdminSettingsStore"/>
/// on first run — the database is authoritative after that.
/// </summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public SmtpEncryption Encryption { get; set; } = SmtpEncryption.StartTls;

    public string FromAddress { get; set; } = "";

    public string FromName { get; set; } = "UpdateWatch2";

    /// <summary>
    /// Where automated alert emails go — the CA-root/server-certificate
    /// expiry warning (<see cref="Certificates.CertificateExpiryWorker"/>)
    /// today, and the still-unimplemented update-threshold notification
    /// CLAUDE.md already anticipates, tomorrow. Deliberately separate from
    /// <see cref="FromAddress"/>: that one is this server's own identity as
    /// a sender, this one is an admin's own inbox (or a distribution list)
    /// as the destination — the two are unrelated addresses that happen to
    /// often belong to the same organization. Empty/unset means "nothing
    /// configured to receive these" — CertificateExpiryWorker still runs
    /// its checks and self-heals (the server leaf still auto-renews) either
    /// way, it just has nowhere to email about it.
    /// </summary>
    public string? NotificationRecipientAddress { get; set; }

    /// <summary>
    /// The externally-reachable base URL of this UpdateWatch2 instance
    /// (e.g. <c>https://updatewatch2.example.com</c> or
    /// <c>http://172.16.12.3:8795</c>), at the user's explicit request
    /// ("Füge in jede Mail bitte noch einen Link zu der Instanz hinzu.").
    /// Used to add a "open UpdateWatch2" link/button to every automated
    /// notification email (<see cref="EmailTemplate"/>) — deliberately a
    /// separate, admin-entered setting rather than guessed from
    /// <c>UPDATEWATCH2_SERVER_HOSTNAME</c>/<c>Kestrel:HttpPort</c>, since
    /// this codebase already documents that it cannot know whether a
    /// reverse proxy in front terminates TLS (the browser-facing port
    /// itself is always plain HTTP — see the auth-cookie <c>SecurePolicy</c>
    /// note in CLAUDE.md) or under what public hostname the instance is
    /// actually reachable. Null/empty means no link is added — every
    /// email still sends normally either way, exactly like an unset
    /// <see cref="NotificationRecipientAddress"/> doesn't block the checks
    /// themselves.
    /// </summary>
    public string? InstanceUrl { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>
/// Independent (OR-combined) thresholds that trigger an update notification
/// email: either crossing updates-per-machine or affected-machine count
/// fires the notification. Each half also has its own independent on/off
/// checkbox (<see cref="UpdatesPerMachineEnabled"/>/<see cref="AffectedMachinesEnabled"/>,
/// both default true) — an admin can rely on just one of the two
/// conditions without the other one ever firing, rather than only being
/// able to turn the whole mechanism on or off at once. See
/// <see cref="UpdateThresholdNotificationWorker"/> for what actually
/// evaluates these.
/// </summary>
public class NotificationThresholdOptions
{
    public const string SectionName = "NotificationThresholds";

    public int UpdatesPerMachine { get; set; } = 5;

    public bool UpdatesPerMachineEnabled { get; set; } = true;

    public int AffectedMachines { get; set; } = 10;

    public bool AffectedMachinesEnabled { get; set; } = true;
}
