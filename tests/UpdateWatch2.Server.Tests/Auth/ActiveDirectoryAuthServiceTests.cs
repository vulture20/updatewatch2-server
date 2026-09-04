using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Tests.Auth;

public class ActiveDirectoryAuthServiceTests
{
    // A host/IP that is guaranteed to be unreachable, in the TEST-NET-1
    // documentation range (RFC 5737) so it never resolves to a real
    // directory. Any test that reaches an actual LdapConnection.Bind()
    // call against this host would hang until TCP connect times out
    // (tens of seconds) rather than fail fast — that's the tell used
    // below to prove the empty-password guard runs *before* any bind is
    // attempted, not to prove connectivity behavior itself.
    private static readonly AdOptions EnabledOptions = new()
    {
        Enabled = true,
        Host = "192.0.2.1",
        Port = 389,
        BindDn = "cn=svc,dc=example,dc=com",
        BindPassword = "svc-password",
        BaseDn = "dc=example,dc=com",
        UserSearchFilter = "(&(objectClass=user)(sAMAccountName={0}))",
        LoginGroupDn = "cn=admins,dc=example,dc=com",
    };

    [Fact]
    public async Task Rejects_an_empty_password_without_attempting_any_LDAP_bind()
    {
        // Regression test for an authentication-bypass finding: RFC 4513
        // §5.1.2 defines a simple bind with a non-empty DN but a
        // zero-length password as an *unauthenticated* bind, which a
        // compliant LDAP server accepts without checking the password at
        // all. If the service ever reached LdapConnection.Bind(userDn, "")
        // for the user-password check, that bind would "succeed" and let
        // anyone log in as any valid/guessed directory username with an
        // empty password. The fix rejects an empty password up front, so
        // this must return a failure immediately — not merely eventually —
        // which this test proves by pointing at an unreachable host: if the
        // guard were missing or removed, this test would hang/time out
        // instead of failing fast, rather than silently passing.
        var service = new ActiveDirectoryAuthService(new FakeAdminSettingsStore(EnabledOptions), NullLogger<ActiveDirectoryAuthService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await service.AuthenticateAsync("someuser", "", cts.Token);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Rejects_a_null_or_whitespace_password_without_attempting_any_LDAP_bind()
    {
        var service = new ActiveDirectoryAuthService(new FakeAdminSettingsStore(EnabledOptions), NullLogger<ActiveDirectoryAuthService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await service.AuthenticateAsync("someuser", null!, cts.Token);

        Assert.False(result.Success);
    }

    /// <summary>Only <see cref="Ad"/> is exercised by this service; everything else throws if touched.</summary>
    private class FakeAdminSettingsStore(AdOptions ad) : IAdminSettingsStore
    {
        public AdOptions Ad => ad;

        public BruteForceOptions BruteForce => throw new NotSupportedException();

        public SmtpOptions Smtp => throw new NotSupportedException();

        public NotificationThresholdOptions NotificationThresholds => throw new NotSupportedException();

        public CertificateOptions Certificate => throw new NotSupportedException();

        public string LogLevel => throw new NotSupportedException();

        public Task InitializeAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AdminSettingsDto> UpdateAsync(UpdateAdminSettingsRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public AdminSettingsDto ToDto() => throw new NotSupportedException();
    }
}
