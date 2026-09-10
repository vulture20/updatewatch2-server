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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>
/// Independent (OR-combined) thresholds that trigger an update notification
/// email: either crossing updates-per-machine or affected-machine count
/// fires the notification.
/// </summary>
public class NotificationThresholdOptions
{
    public const string SectionName = "NotificationThresholds";

    public int UpdatesPerMachine { get; set; } = 5;

    public int AffectedMachines { get; set; } = 10;
}
