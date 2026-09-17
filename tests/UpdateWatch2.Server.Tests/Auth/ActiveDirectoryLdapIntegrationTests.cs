using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.AgentUpdates;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Tests.Auth;

/// <summary>
/// Exercises <see cref="ActiveDirectoryAuthService"/>'s actual bind/search/
/// group-membership logic against a real LDAP server (updatewatch2-server#13)
/// — the piece <see cref="ActiveDirectoryAuthServiceTests"/> deliberately
/// never touches (that class only proves the empty-password guard runs
/// before any bind is attempted, against a guaranteed-unreachable host).
/// Reproduces the exact four-case scenario this project's own CLAUDE.md
/// already documents as having been verified once by hand against a real
/// <c>docker run osixia/openldap:1.5.0</c> — now automated.
///
/// Needs a real LDAP server actually running on <see cref="Port"/>
/// (<c>scripts/run-ldap-test-server.sh up</c>, or the dedicated
/// <c>ldap-integration-test</c> CI job that runs it automatically) —
/// tagged with a Trait rather than run by default, so an ordinary
/// <c>dotnet test</c> (no LDAP server, no Docker requirement) never fails
/// or hangs on these. Run explicitly with
/// <c>dotnet test --filter Category=LdapIntegration</c>.
/// </summary>
[Trait("Category", "LdapIntegration")]
public class ActiveDirectoryLdapIntegrationTests
{
    // Matches scripts/run-ldap-test-server.sh's own default — overridable
    // via the same env var for a local dev machine where 389 is already
    // taken.
    private static readonly int Port = int.TryParse(Environment.GetEnvironmentVariable("UPDATEWATCH2_TEST_LDAP_PORT"), out var p) ? p : 389;

    // dc=example,dc=org / the admin bind DN / the OU layout / alice+bob+admins
    // all come from tests/UpdateWatch2.Server.Tests/Auth/ldap-seed/bootstrap.ldif
    // and scripts/run-ldap-test-server.sh's own LDAP_DOMAIN/LDAP_ADMIN_PASSWORD —
    // change one, change the other.
    private static readonly AdOptions Options = new()
    {
        Enabled = true,
        Host = "127.0.0.1",
        Port = Port,
        // AdOptions.Encryption defaults to StartTls — this throwaway
        // container has no TLS cert configured (LDAP_TLS=false in
        // scripts/run-ldap-test-server.sh), so this must be explicit
        // (found the hard way: StartTransportLayerSecurity fails with
        // "The LDAP server is unavailable" against a plaintext-only
        // server, surfaced up as the same generic "Could not connect to
        // the directory server." the real unreachable-host case produces
        // — worth remembering if this test ever mysteriously "can't
        // connect" again despite the container clearly running).
        Encryption = AdEncryption.None,
        BindDn = "cn=admin,dc=example,dc=org",
        BindPassword = "admin-p@ssw0rd",
        BaseDn = "dc=example,dc=org",
        // OpenLDAP's own schema, not the AD-shaped default — matches
        // CLAUDE.md's own note on how the manual verification run set
        // this same field for the same reason.
        UserSearchFilter = "(&(objectClass=inetOrgPerson)(uid={0}))",
        LoginGroupDn = "cn=admins,ou=groups,dc=example,dc=org",
    };

    [Fact]
    public async Task A_group_member_with_the_correct_password_succeeds()
    {
        var service = CreateService();

        var result = await service.AuthenticateAsync("alice", "alice-p@ssw0rd");

        Assert.True(result.Success);
        Assert.Equal("Alice Example", result.DisplayName);
    }

    [Fact]
    public async Task A_group_member_with_the_wrong_password_fails()
    {
        var service = CreateService();

        var result = await service.AuthenticateAsync("alice", "not-her-password");

        Assert.False(result.Success);
    }

    /// <summary>
    /// The case that actually matters: a real, correctly-authenticated
    /// directory user who simply isn't in the configured login group.
    /// Nothing about a successful LDAP bind alone can ever catch this —
    /// only the separate group-membership search does.
    /// </summary>
    [Fact]
    public async Task A_correctly_authenticated_user_who_is_not_a_group_member_fails()
    {
        var service = CreateService();

        var result = await service.AuthenticateAsync("bob", "bob-p@ssw0rd");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task An_unknown_username_fails()
    {
        var service = CreateService();

        var result = await service.AuthenticateAsync("charlie", "whatever");

        Assert.False(result.Success);
    }

    private static ActiveDirectoryAuthService CreateService() =>
        new(new FakeAdminSettingsStore(Options), NullLogger<ActiveDirectoryAuthService>.Instance);

    /// <summary>Only <see cref="Ad"/> is exercised by this service; everything else throws if touched.</summary>
    private class FakeAdminSettingsStore(AdOptions ad) : IAdminSettingsStore
    {
        public AdOptions Ad => ad;

        public BruteForceOptions BruteForce => throw new NotSupportedException();

        public SmtpOptions Smtp => throw new NotSupportedException();

        public NotificationThresholdOptions NotificationThresholds => throw new NotSupportedException();

        public CertificateOptions Certificate => throw new NotSupportedException();

        public AgentAutoUpdateOptions AgentAutoUpdate => throw new NotSupportedException();

        public UpdateWatch2.Server.Agents.AgentOfflineOptions AgentOffline => throw new NotSupportedException();

        public string LogLevel => throw new NotSupportedException();

        public int AuditLogRetentionDays => throw new NotSupportedException();

        public bool PreDownloadWindowsUpdatesEnabled => throw new NotSupportedException();
        public bool PreDownloadLinuxUpdatesEnabled => throw new NotSupportedException();

        public Task InitializeAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AdminSettingsDto> UpdateAsync(UpdateAdminSettingsRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public AdminSettingsDto ToDto() => throw new NotSupportedException();
    }
}
