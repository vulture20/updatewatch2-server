using UpdateWatch2.Server.Certificates;

namespace UpdateWatch2.Server.Tests.Certificates;

public class CertificateRejectionClassifierTests : IDisposable
{
    private readonly string _certsDirectory = Path.Combine(Path.GetTempPath(), $"uw2-rejection-classifier-certs-{Guid.NewGuid()}");
    private readonly InternalCertificateAuthority _ca;

    public CertificateRejectionClassifierTests()
    {
        _ca = new InternalCertificateAuthority(_certsDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_certsDirectory))
        {
            Directory.Delete(_certsDirectory, recursive: true);
        }
    }

    [Fact]
    public void Classify_returns_Expired_for_a_certificate_past_its_NotAfter()
    {
        // IssueAgentLeaf backdates NotBefore by 5 minutes (clock-skew
        // tolerance — see InternalCertificateAuthority.IssueAgentLeaf), so a
        // 1ms validity window is already ~5 minutes in the past the moment
        // it's issued, no need to wait for it to expire.
        var issued = _ca.IssueAgentLeaf("expired-host", TimeSpan.FromMilliseconds(1));
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(issued.PfxBytes, password: null);

        var reason = CertificateRejectionClassifier.Classify(cert);

        Assert.Equal(CertificateRejectionReason.Expired, reason);
    }

    [Fact]
    public void Classify_returns_NotYetValid_for_a_certificate_before_its_NotBefore()
    {
        // InternalCertificateAuthority always backdates NotBefore by 5
        // minutes, so a not-yet-valid certificate can't come from it —
        // built directly here instead.
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=future-host", key, System.Security.Cryptography.HashAlgorithmName.SHA256);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2));

        var reason = CertificateRejectionClassifier.Classify(cert);

        Assert.Equal(CertificateRejectionReason.NotYetValid, reason);
    }

    [Fact]
    public void Classify_returns_NotTrusted_for_a_currently_valid_certificate()
    {
        var issued = _ca.IssueAgentLeaf("valid-host", TimeSpan.FromDays(730));
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(issued.PfxBytes, password: null);

        // Within its own validity period — if this path is reached at all
        // (OnAuthenticationFailed only fires for a certificate the handler
        // itself rejected), a validity-period-valid certificate must have
        // failed on chain trust instead, not expiry.
        var reason = CertificateRejectionClassifier.Classify(cert);

        Assert.Equal(CertificateRejectionReason.NotTrusted, reason);
    }

    [Fact]
    public void Classify_returns_NotTrusted_for_a_null_certificate()
    {
        var reason = CertificateRejectionClassifier.Classify(null);

        Assert.Equal(CertificateRejectionReason.NotTrusted, reason);
    }
}
