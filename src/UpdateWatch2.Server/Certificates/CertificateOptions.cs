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
}
