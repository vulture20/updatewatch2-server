using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace UpdateWatch2.Server.Certificates;

/// <summary>
/// Self-signed internal CA, generated on first run and persisted to
/// <c>&lt;certs directory&gt;/ca.pfx</c> — the same volume-backed,
/// unencrypted-at-rest trust boundary used for Data Protection keys (see
/// Program.cs's comment on <c>PersistKeysToFileSystem</c>): filesystem/volume
/// access control is the actual protection, not a password prompt, because
/// there's nowhere secret to put a password that survives a container
/// restart without a separate operator step this project has deliberately
/// avoided elsewhere too. No external CA/ACME integration — an internal CA
/// is sufficient because the only parties that ever need to trust it are
/// this server and its own agents, not a public client.
///
/// Root: RSA 4096, 10-year validity — long enough that rotation is a rare,
/// deliberately out-of-scope, separate concern (see the follow-up issue
/// opened alongside this feature). Leaves (server + agent): ECDsa P-256,
/// short-lived by comparison (2 years — see <see cref="AgentLeafValidity"/>),
/// cheap to generate, cross-signed under the RSA root via
/// <see cref="CertificateRequest.Create(X509Certificate2, DateTimeOffset, DateTimeOffset, byte[])"/>,
/// a standard, fully-supported .NET pattern. Leaf renewal-before-expiry is
/// also deliberately out of scope here — another follow-up issue.
///
/// The server's own leaf (<see cref="EnsureServerLeaf"/>) is regenerated
/// automatically whenever its SAN no longer matches the currently configured
/// hostname, rather than requiring an operator to manually delete
/// <c>server.pfx</c> — safe to do freely because agents validate the *chain
/// to the pinned root*, not the leaf's identity/thumbprint, so rotating the
/// leaf never breaks an already-onboarded agent.
/// </summary>
public class InternalCertificateAuthority : ICertificateAuthority
{
    private const int RootKeySizeBits = 4096;
    private static readonly TimeSpan RootValidity = TimeSpan.FromDays(365 * 10);
    private static readonly TimeSpan ServerLeafValidity = TimeSpan.FromDays(365 * 2);
    private static readonly TimeSpan AgentLeafValidity = TimeSpan.FromDays(365 * 2);

    // Server Authentication / Client Authentication, per RFC 5280 appendix.
    private static readonly Oid ServerAuthEku = new("1.3.6.1.5.5.7.3.1");
    private static readonly Oid ClientAuthEku = new("1.3.6.1.5.5.7.3.2");

    private readonly string _certsDirectory;
    private readonly Lock _issueLock = new();

    public InternalCertificateAuthority(string certsDirectory)
    {
        _certsDirectory = certsDirectory;
        Directory.CreateDirectory(_certsDirectory);
        RootCertificate = LoadOrCreateRoot();
    }

    public X509Certificate2 RootCertificate { get; }

    public X509Certificate2 EnsureServerLeaf(string sanHostname)
    {
        var path = Path.Combine(_certsDirectory, "server.pfx");
        if (File.Exists(path))
        {
            var existing = X509CertificateLoader.LoadPkcs12FromFile(path, password: null, X509KeyStorageFlags.Exportable);
            if (HasSan(existing, sanHostname))
            {
                return existing;
            }

            // The configured hostname changed since this leaf was issued —
            // regenerate rather than fail; see the class-level remarks on why
            // this is always safe.
            existing.Dispose();
        }

        var (cert, pfxBytes) = CreateLeaf(sanHostname, [sanHostname], ServerAuthEku, ServerLeafValidity);
        File.WriteAllBytes(path, pfxBytes);
        RestrictToOwner(path);
        return cert;
    }

    public IssuedCertificate IssueAgentLeaf(string hostname)
    {
        lock (_issueLock)
        {
            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var notAfter = notBefore.Add(AgentLeafValidity);
            var (cert, pfxBytes) = CreateLeaf(hostname, [], ClientAuthEku, AgentLeafValidity, notBefore, notAfter);
            var thumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256);
            cert.Dispose();
            return new IssuedCertificate(pfxBytes, thumbprint, notBefore, notAfter);
        }
    }

    private X509Certificate2 LoadOrCreateRoot()
    {
        var path = Path.Combine(_certsDirectory, "ca.pfx");
        if (File.Exists(path))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(path, password: null, X509KeyStorageFlags.Exportable);
        }

        using var rsa = RSA.Create(RootKeySizeBits);
        var request = new CertificateRequest(
            new X500DistinguishedName("CN=UpdateWatch2 Internal CA"),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.Add(RootValidity);
        using var selfSigned = request.CreateSelfSigned(notBefore, notAfter);

        var pfxBytes = selfSigned.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, pfxBytes);
        RestrictToOwner(path);

        return X509CertificateLoader.LoadPkcs12(pfxBytes, password: null, X509KeyStorageFlags.Exportable);
    }

    private (X509Certificate2 Cert, byte[] PfxBytes) CreateLeaf(
        string subjectCn, IReadOnlyList<string> sanDnsNames, Oid enhancedKeyUsage, TimeSpan validity) =>
        CreateLeaf(subjectCn, sanDnsNames, enhancedKeyUsage, validity, DateTimeOffset.UtcNow.AddMinutes(-5), null);

    private (X509Certificate2 Cert, byte[] PfxBytes) CreateLeaf(
        string subjectCn, IReadOnlyList<string> sanDnsNames, Oid enhancedKeyUsage, TimeSpan validity,
        DateTimeOffset notBefore, DateTimeOffset? notAfterOverride)
    {
        var notAfter = notAfterOverride ?? notBefore.Add(validity);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(new X500DistinguishedName($"CN={subjectCn}"), ecdsa, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([enhancedKeyUsage], critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        if (sanDnsNames.Count > 0)
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (var name in sanDnsNames)
            {
                sanBuilder.AddDnsName(name);
            }

            request.CertificateExtensions.Add(sanBuilder.Build());
        }

        var serialNumber = RandomNumberGenerator.GetBytes(16);

        // The convenience CertificateRequest.Create(X509Certificate2, ...)
        // overload requires the issuer and the leaf to use the same key
        // algorithm (confirmed by hand: it throws "issuer certificate public
        // key algorithm does not match" here, since the root is RSA and every
        // leaf is ECDsa by design — see the class-level remarks). The
        // X509SignatureGenerator overload is the one that actually supports
        // signing an ECDsa leaf with an RSA-keyed issuer.
        using var issuerKey = RootCertificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Root CA certificate has no RSA private key to sign with.");
        var signatureGenerator = X509SignatureGenerator.CreateForRSA(issuerKey, RSASignaturePadding.Pkcs1);
        using var publicOnly = request.Create(RootCertificate.SubjectName, signatureGenerator, notBefore, notAfter, serialNumber);
        using var withKey = publicOnly.CopyWithPrivateKey(ecdsa);

        var pfxBytes = withKey.Export(X509ContentType.Pfx);
        var reloaded = X509CertificateLoader.LoadPkcs12(pfxBytes, password: null, X509KeyStorageFlags.Exportable);
        return (reloaded, pfxBytes);
    }

    private static bool HasSan(X509Certificate2 cert, string hostname) =>
        cert.Extensions.OfType<X509SubjectAlternativeNameExtension>()
            .Any(ext => ext.EnumerateDnsNames().Contains(hostname, StringComparer.OrdinalIgnoreCase));

    private static void RestrictToOwner(string path)
    {
        // Meaningful once the container/service runs as a dedicated non-root
        // account (already true for the Docker image — see docker/Dockerfile's
        // `app` user); a no-op restriction on Windows, where the equivalent
        // would be an ACL change this project doesn't make.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
