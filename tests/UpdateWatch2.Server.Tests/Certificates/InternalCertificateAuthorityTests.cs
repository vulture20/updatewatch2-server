using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UpdateWatch2.Server.Certificates;

namespace UpdateWatch2.Server.Tests.Certificates;

public class InternalCertificateAuthorityTests : IDisposable
{
    private readonly string _certsDirectory = Path.Combine(Path.GetTempPath(), $"uw2-certs-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_certsDirectory))
        {
            Directory.Delete(_certsDirectory, recursive: true);
        }
    }

    [Fact]
    public void Root_certificate_is_a_self_signed_CA()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);

        var basicConstraints = ca.RootCertificate.Extensions.OfType<X509BasicConstraintsExtension>().Single();
        Assert.True(basicConstraints.CertificateAuthority);
        Assert.Equal(ca.RootCertificate.Subject, ca.RootCertificate.Issuer);
        Assert.True(File.Exists(Path.Combine(_certsDirectory, "ca.pfx")));
    }

    [Fact]
    public void Root_certificate_is_reloaded_unchanged_across_instances()
    {
        var first = new InternalCertificateAuthority(_certsDirectory);
        var second = new InternalCertificateAuthority(_certsDirectory);

        Assert.Equal(first.RootCertificate.Thumbprint, second.RootCertificate.Thumbprint);
    }

    [Fact]
    public void Server_leaf_chains_to_the_root_and_carries_the_requested_SAN()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);

        var leaf = ca.EnsureServerLeaf("updatewatch2.example.com");

        var sanExtension = leaf.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Contains("updatewatch2.example.com", sanExtension.EnumerateDnsNames());

        var eku = leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        Assert.Contains(eku.EnhancedKeyUsages.Cast<Oid>(), oid => oid.Value == "1.3.6.1.5.5.7.3.1"); // Server Authentication

        AssertChainsToRoot(leaf, ca.RootCertificate);
    }

    [Fact]
    public void Server_leaf_is_regenerated_when_the_configured_hostname_changes()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);
        var original = ca.EnsureServerLeaf("old-hostname.example.com");

        var regenerated = ca.EnsureServerLeaf("new-hostname.example.com");

        Assert.NotEqual(original.Thumbprint, regenerated.Thumbprint);
        var sanExtension = regenerated.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Contains("new-hostname.example.com", sanExtension.EnumerateDnsNames());
    }

    [Fact]
    public void Server_leaf_is_reused_unchanged_when_the_hostname_is_unchanged()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);
        var first = ca.EnsureServerLeaf("stable-hostname.example.com");

        var second = ca.EnsureServerLeaf("stable-hostname.example.com");

        Assert.Equal(first.Thumbprint, second.Thumbprint);
    }

    [Fact]
    public void Issued_agent_leaf_chains_to_the_root_carries_client_auth_EKU_and_a_matching_SHA256_thumbprint()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);

        var issued = ca.IssueAgentLeaf("workstation-42");

        using var cert = X509CertificateLoader.LoadPkcs12(issued.PfxBytes, password: null);
        Assert.Equal("CN=workstation-42", cert.Subject);
        Assert.Equal(issued.ThumbprintSha256, cert.GetCertHashString(HashAlgorithmName.SHA256));

        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        Assert.Contains(eku.EnhancedKeyUsages.Cast<Oid>(), oid => oid.Value == "1.3.6.1.5.5.7.3.2"); // Client Authentication

        AssertChainsToRoot(cert, ca.RootCertificate);

        Assert.True(issued.ExpiresAt > issued.IssuedAt);
        Assert.True(issued.ExpiresAt - issued.IssuedAt > TimeSpan.FromDays(365)); // ~2-year validity
    }

    [Fact]
    public void Two_agent_leaves_for_different_hostnames_get_different_thumbprints()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);

        var a = ca.IssueAgentLeaf("host-a");
        var b = ca.IssueAgentLeaf("host-b");

        Assert.NotEqual(a.ThumbprintSha256, b.ThumbprintSha256);
    }

    private static void AssertChainsToRoot(X509Certificate2 leaf, X509Certificate2 root)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        var built = chain.Build(leaf);
        Assert.True(built, string.Join(", ", chain.ChainStatus.Select(s => s.StatusInformation)));
    }
}
