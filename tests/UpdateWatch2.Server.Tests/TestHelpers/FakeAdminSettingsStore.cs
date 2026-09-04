using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Tests.TestHelpers;

/// <summary>
/// A minimal hand-written IAdminSettingsStore fake for tests that only
/// care about one settings group (e.g. agent certificate validity) —
/// every other member throws, so a test that accidentally depends on an
/// unconfigured group fails loudly rather than silently reading a
/// meaningless default. <see cref="Certificate"/> defaults to the same
/// 730-day value as the real <see cref="CertificateOptions"/>, settable
/// per test via the constructor.
/// </summary>
public class FakeAdminSettingsStore(CertificateOptions? certificate = null) : IAdminSettingsStore
{
    // Settable, not init-only: a test can change this mid-test (e.g. to
    // assert a subsequent issuance/renewal picks up a new validity) rather
    // than needing to reconstruct the service under test.
    public CertificateOptions Certificate { get; set; } = certificate ?? new CertificateOptions();

    public BruteForceOptions BruteForce => throw new NotSupportedException();

    public SmtpOptions Smtp => throw new NotSupportedException();

    public NotificationThresholdOptions NotificationThresholds => throw new NotSupportedException();

    public AdOptions Ad => throw new NotSupportedException();

    public string LogLevel => throw new NotSupportedException();

    public Task InitializeAsync(CancellationToken ct = default) => throw new NotSupportedException();

    public Task<AdminSettingsDto> UpdateAsync(UpdateAdminSettingsRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public AdminSettingsDto ToDto() => throw new NotSupportedException();
}
