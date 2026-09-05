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

        var issued = ca.IssueAgentLeaf("workstation-42", TimeSpan.FromDays(730));

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
    public void Issued_agent_leaf_honors_the_requested_validity_period()
    {
        // Direct proof of the admin-configurable-validity plumbing
        // (updatewatch2-server#9) — not just that a fixed ~2-year default
        // still comes out, but that whatever the caller asks for is what
        // gets stamped onto the certificate.
        var ca = new InternalCertificateAuthority(_certsDirectory);

        var issued = ca.IssueAgentLeaf("short-lived-host", TimeSpan.FromDays(30));

        var actualValidity = issued.ExpiresAt - issued.IssuedAt;
        Assert.True(Math.Abs((actualValidity - TimeSpan.FromDays(30)).TotalMinutes) < 1);
    }

    [Fact]
    public void Two_agent_leaves_for_different_hostnames_get_different_thumbprints()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);

        var a = ca.IssueAgentLeaf("host-a", TimeSpan.FromDays(730));
        var b = ca.IssueAgentLeaf("host-b", TimeSpan.FromDays(730));

        Assert.NotEqual(a.ThumbprintSha256, b.ThumbprintSha256);
    }

    [Fact]
    public void PrepareRotation_generates_a_pending_root_without_disturbing_the_current_one()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);
        var currentBefore = ca.RootCertificate.GetCertHashString(HashAlgorithmName.SHA256);

        var pending = ca.PrepareRotation();

        Assert.Equal(currentBefore, ca.RootCertificate.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.NotEqual(currentBefore, pending.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.Equal(pending.GetCertHashString(HashAlgorithmName.SHA256), ca.PendingRootCertificate!.GetCertHashString(HashAlgorithmName.SHA256));

        // Not yet trusted for client-cert authentication — nothing is
        // signed by it yet.
        Assert.DoesNotContain(ca.TrustedRootCertificates.Cast<X509Certificate2>(),
            c => c.GetCertHashString(HashAlgorithmName.SHA256) == pending.GetCertHashString(HashAlgorithmName.SHA256));

        // But published in the full bundle, so an agent can pre-trust it.
        Assert.Contains(ca.AllKnownRootCertificates.Cast<X509Certificate2>(),
            c => c.GetCertHashString(HashAlgorithmName.SHA256) == pending.GetCertHashString(HashAlgorithmName.SHA256));
    }

    [Fact]
    public void ActivateRotation_throws_when_nothing_is_pending()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);

        Assert.Throws<InvalidOperationException>(() => ca.ActivateRotation());
    }

    [Fact]
    public void ActivateRotation_promotes_the_pending_root_and_keeps_the_old_one_trusted_as_previous()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);
        var originalRootThumbprint = ca.RootCertificate.GetCertHashString(HashAlgorithmName.SHA256);
        var pending = ca.PrepareRotation();
        var pendingThumbprint = pending.GetCertHashString(HashAlgorithmName.SHA256);

        // An agent leaf issued before activation must keep validating
        // afterward — this is the entire point of "previous" staying
        // trusted rather than the CA simply swapping roots outright.
        var preRotationLeaf = ca.IssueAgentLeaf("pre-rotation-host", TimeSpan.FromDays(730));

        ca.ActivateRotation();

        Assert.Equal(pendingThumbprint, ca.RootCertificate.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.Equal(originalRootThumbprint, ca.PreviousRootCertificate!.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.Null(ca.PendingRootCertificate);

        using var preRotationCert = X509CertificateLoader.LoadPkcs12(preRotationLeaf.PfxBytes, password: null);
        AssertChainsToAnyOf(preRotationCert, ca.TrustedRootCertificates);

        // A leaf issued AFTER activation is signed by the new current root.
        var postRotationLeaf = ca.IssueAgentLeaf("post-rotation-host", TimeSpan.FromDays(730));
        using var postRotationCert = X509CertificateLoader.LoadPkcs12(postRotationLeaf.PfxBytes, password: null);
        AssertChainsToRoot(postRotationCert, ca.RootCertificate);
    }

    [Fact]
    public void ActivateRotation_reissues_the_server_leaf_under_the_new_root_immediately()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);
        ca.EnsureServerLeaf("updatewatch2.example.com");
        var leafBeforeThumbprint = ca.CurrentServerLeaf.GetCertHashString(HashAlgorithmName.SHA256);

        ca.PrepareRotation();
        ca.ActivateRotation();

        // No EnsureServerLeaf call in between — ActivateRotation itself must
        // have re-issued it, matching the "no restart required" bar this
        // project holds re-issuance to.
        Assert.NotEqual(leafBeforeThumbprint, ca.CurrentServerLeaf.GetCertHashString(HashAlgorithmName.SHA256));
        AssertChainsToRoot(ca.CurrentServerLeaf, ca.RootCertificate);

        var sanExtension = ca.CurrentServerLeaf.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Contains("updatewatch2.example.com", sanExtension.EnumerateDnsNames());
    }

    [Fact]
    public void RetirePreviousRoot_throws_when_there_is_no_previous_root()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);

        Assert.Throws<InvalidOperationException>(() => ca.RetirePreviousRoot());
    }

    [Fact]
    public void RetirePreviousRoot_drops_it_from_trust_and_a_leaf_signed_only_by_it_no_longer_validates()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);
        var preRotationLeaf = ca.IssueAgentLeaf("stranded-host", TimeSpan.FromDays(730));
        ca.PrepareRotation();
        ca.ActivateRotation();

        using var strandedCert = X509CertificateLoader.LoadPkcs12(preRotationLeaf.PfxBytes, password: null);
        AssertChainsToAnyOf(strandedCert, ca.TrustedRootCertificates); // still trusted via "previous" so far

        ca.RetirePreviousRoot();

        Assert.Null(ca.PreviousRootCertificate);
        Assert.False(BuildsChain(strandedCert, ca.TrustedRootCertificates), "A leaf signed only by the retired root must no longer chain-validate.");
    }

    [Fact]
    public void ActivateRotation_a_second_time_discards_the_older_previous_root_keeping_only_two_generations()
    {
        var ca = new InternalCertificateAuthority(_certsDirectory);
        var firstRootThumbprint = ca.RootCertificate.GetCertHashString(HashAlgorithmName.SHA256);
        ca.PrepareRotation();
        ca.ActivateRotation(); // previous = original first root

        ca.PrepareRotation();
        ca.ActivateRotation(); // previous should now be the SECOND root, not the first

        Assert.NotEqual(firstRootThumbprint, ca.PreviousRootCertificate!.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.DoesNotContain(ca.TrustedRootCertificates.Cast<X509Certificate2>(),
            c => c.GetCertHashString(HashAlgorithmName.SHA256) == firstRootThumbprint);
    }

    [Fact]
    public void Rotation_state_survives_a_reload_from_disk()
    {
        var first = new InternalCertificateAuthority(_certsDirectory);
        first.PrepareRotation();
        first.ActivateRotation();
        var currentAfterActivation = first.RootCertificate.GetCertHashString(HashAlgorithmName.SHA256);
        var previousAfterActivation = first.PreviousRootCertificate!.GetCertHashString(HashAlgorithmName.SHA256);

        var second = new InternalCertificateAuthority(_certsDirectory);

        Assert.Equal(currentAfterActivation, second.RootCertificate.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.Equal(previousAfterActivation, second.PreviousRootCertificate!.GetCertHashString(HashAlgorithmName.SHA256));
    }

    private static void AssertChainsToAnyOf(X509Certificate2 leaf, X509Certificate2Collection roots) =>
        Assert.True(BuildsChain(leaf, roots), "Expected the leaf to chain to at least one of the given roots.");

    private static bool BuildsChain(X509Certificate2 leaf, X509Certificate2Collection roots)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(roots);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(leaf);
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
