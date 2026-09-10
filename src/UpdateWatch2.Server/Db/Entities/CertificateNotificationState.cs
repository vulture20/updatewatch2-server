namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// The single, persisted row <see cref="Certificates.CertificateExpiryWorker"/>
/// uses to avoid re-sending the same email on every check — the same
/// one-row-table convention <see cref="AdminSettings"/>/<see cref="AgentUpdateState"/>
/// already use. Tracked by thumbprint, not just a timestamp: a CA
/// rotation or a server-leaf renewal produces a genuinely new certificate
/// with its own future expiry, and that new certificate's eventual
/// approach-of-expiry deserves its own fresh warning, so "already warned"
/// has to mean "already warned about THIS specific certificate", not
/// "warned at some point".
/// </summary>
public class CertificateNotificationState
{
    public int Id { get; set; }

    /// <summary>
    /// SHA-256 thumbprint of the CA root a "will expire soon" warning was
    /// last sent for. Null until the first warning ever fires.
    /// </summary>
    public string? LastCaWarningThumbprint { get; set; }

    public DateTimeOffset? LastCaWarningSentAt { get; set; }

    /// <summary>
    /// SHA-256 thumbprint of the server leaf <see cref="CertificateExpiryWorker"/>
    /// most recently auto-renewed via
    /// <see cref="Certificates.ICertificateAuthority.RenewServerLeafIfNearExpiry"/> —
    /// not necessarily the one currently in use, since a later hostname
    /// change or CA rotation can replace it again without this worker's
    /// involvement. Kept (rather than only recording that a renewal
    /// happened) so a later tick can retry the notification email below if
    /// the first attempt failed — <c>RenewServerLeafIfNearExpiry</c> itself
    /// only fires once per renewal, so nothing else would re-surface it.
    /// </summary>
    public string? LastServerLeafRenewalThumbprint { get; set; }

    /// <summary>The renewed leaf's own <c>NotAfter</c>, captured alongside the thumbprint above so a delayed retry can still compose an accurate notification email.</summary>
    public DateTimeOffset? LastServerLeafRenewalNotAfter { get; set; }

    /// <summary>
    /// Null means "a renewal above is still waiting on a successful
    /// notification attempt" (or there was never a recipient configured to
    /// notify, in which case this is set immediately since there's nothing
    /// to retry) — the same "audit-log/email atomically, only once actually
    /// handled" pattern <see cref="LastCaWarningSentAt"/> uses.
    /// </summary>
    public DateTimeOffset? LastServerLeafRenewalNotifiedAt { get; set; }
}
