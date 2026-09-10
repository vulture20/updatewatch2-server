namespace UpdateWatch2.Server.Certificates;

/// <summary>
/// Admin-configurable agent client certificate validity (updatewatch2-server#9).
/// Bound from appsettings.json's "Certificate" section only as the
/// compiled-in default used to seed <see cref="UpdateWatch2.Server.Admin.AdminSettingsStore"/>
/// on first run — the database is authoritative after that, same as
/// <see cref="Auth.BruteForceOptions"/>/<see cref="Notifications.SmtpOptions"/>.
///
/// Deliberately scoped to the agent leaf only — the CA root and the
/// server's own TLS leaf are both generated in Program.cs before the DB is
/// migrated and before <c>AdminSettingsStore.InitializeAsync</c> has run,
/// so there is no live settings store to read from at that point (and
/// changing the root's validity after it's already been generated
/// wouldn't be retroactive anyway — see the CA-rotation follow-up issue).
/// Agent leaf issuance, by contrast, always happens later, at
/// registration/renewal request time inside a normal DI scope, so it can
/// safely read this live.
/// </summary>
public class CertificateOptions
{
    public const string SectionName = "Certificate";

    public int AgentCertificateValidityDays { get; set; } = 730;

    /// <summary>
    /// How many days before <c>NotAfter</c> <see cref="CertificateExpiryWorker"/>
    /// treats the CA root/server leaf as "approaching expiry" — matches
    /// the agent's own <c>AgentOptions.CertificateRenewalLeadTimeDays</c>
    /// default (60) for the same reasoning: long enough that an admin (or,
    /// for the server leaf, the worker itself) has real time to act before
    /// anything actually breaks. Unlike <see cref="AgentCertificateValidityDays"/>,
    /// this one IS read live by a DI-scoped worker rather than only at
    /// pre-DI startup, so the "no live settings store yet" caveat on this
    /// class's own doc comment doesn't apply to it.
    /// </summary>
    public int CertificateExpiryWarningLeadDays { get; set; } = 60;

    /// <summary>
    /// Admin on/off switch for the CA-root/server-leaf expiry emails
    /// specifically — at the user's explicit request, an explicit toggle
    /// rather than only the implicit "no recipient configured" off-switch
    /// <see cref="Notifications.SmtpOptions.NotificationRecipientAddress"/>
    /// already provides. Default <see langword="true"/>. Gates
    /// <see cref="CertificateExpiryWorker"/>'s emails only — the server
    /// leaf's own proactive self-renewal (<see cref="ICertificateAuthority.RenewServerLeafIfNearExpiry"/>)
    /// and the audit log entries for both certificates keep happening
    /// unconditionally either way, since those matter independently of
    /// whether anyone gets emailed about them.
    /// </summary>
    public bool CertificateExpiryNotificationsEnabled { get; set; } = true;
}
