namespace UpdateWatch2.Server.Auth;

public enum AdEncryption
{
    None,
    StartTls,
    Ldaps,
}

/// <summary>
/// Active Directory connection configuration (Administration area,
/// CLAUDE.md section 6.1) — a separate login path from the local `admin`
/// account (see <see cref="AdminAccountService"/>), not a replacement for
/// it. Bound from appsettings.json's "Ad" section only as the compiled-in
/// default used to seed <see cref="UpdateWatch2.Server.Admin.AdminSettingsStore"/>
/// on first run — the database is authoritative after that.
/// </summary>
public class AdOptions
{
    public const string SectionName = "Ad";

    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; } = 389;

    public AdEncryption Encryption { get; set; } = AdEncryption.StartTls;

    /// <summary>DN of the service account used to search for the logging-in user.</summary>
    public string BindDn { get; set; } = "";

    public string? BindPassword { get; set; }

    /// <summary>Where in the directory to search for the logging-in user.</summary>
    public string BaseDn { get; set; } = "";

    /// <summary>
    /// LDAP filter template with <c>{0}</c> substituted for the submitted
    /// username. Default matches a standard Active Directory user by
    /// sAMAccountName; a generic LDAP/OpenLDAP directory typically wants
    /// something like <c>(&amp;(objectClass=person)(uid={0}))</c> instead.
    /// </summary>
    public string UserSearchFilter { get; set; } = "(&(objectClass=user)(sAMAccountName={0}))";

    /// <summary>
    /// DN of the group whose members are granted login. Checked via the
    /// group entry's own `member` attribute (works against both real AD —
    /// which maintains `member` alongside the `memberOf` back-link — and
    /// plain `groupOfNames`-style LDAP groups), not `memberOf` on the user,
    /// so no directory-specific overlay is required.
    /// </summary>
    public string LoginGroupDn { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(BaseDn) && !string.IsNullOrWhiteSpace(LoginGroupDn);
}
