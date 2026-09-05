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
/// Root: RSA 4096, 10-year validity — long enough that rotation
/// (updatewatch2-server#6) is a rare, deliberate admin action rather than
/// something this class does on its own. Leaves (server + agent): ECDsa
/// P-256, short-lived by comparison, cheap to generate, cross-signed under
/// the RSA root via
/// <see cref="CertificateRequest.Create(X509Certificate2, DateTimeOffset, DateTimeOffset, byte[])"/>,
/// a standard, fully-supported .NET pattern. The server leaf's validity is
/// still a fixed constant; the agent leaf's is not — <see cref="IssueAgentLeaf"/>
/// takes it as a parameter, sourced by the caller from the live
/// admin-configured <see cref="CertificateOptions.AgentCertificateValidityDays"/>
/// (updatewatch2-server#9). Proactive renewal before expiry and
/// admin-mediated re-issuance after a lost/wiped agent certificate are
/// both implemented too (updatewatch2-server#7/#8) — this class stays
/// unaware of either, it just issues on request.
///
/// The server's own leaf (<see cref="EnsureServerLeaf"/>) is regenerated
/// automatically whenever its SAN no longer matches the currently configured
/// hostname, or it no longer chains to the CURRENT root (which
/// <see cref="ActivateRotation"/> can change at runtime) — rather than
/// requiring an operator to manually delete <c>server.pfx</c>. Safe to do
/// freely because agents validate the *chain to a trusted root*, not the
/// leaf's identity/thumbprint, so rotating the leaf never breaks an
/// already-onboarded agent, provided that agent already trusts the new
/// root — see the class-level remarks on <see cref="ActivateRotation"/> for
/// why that ordering matters.
///
/// Rotation (updatewatch2-server#6) keeps at most three roots on disk at
/// once: <c>ca.pfx</c> (current, signs everything new), <c>ca-previous.pfx</c>
/// (superseded but still trusted for already-issued, not-yet-renewed agent
/// leaves), and <c>ca-next.pfx</c> (prepared but not yet active — published
/// for agents to pre-trust, signs nothing yet). Only two generations are
/// ever kept live (current + previous) — activating again while a previous
/// root still exists discards it outright, on the assumption that any leaf
/// old enough to have been signed two rotations back has had far longer
/// than <c>CertificateRenewalLeadTimeDays</c> to renew already.
/// </summary>
public class InternalCertificateAuthority : ICertificateAuthority
{
    private const int RootKeySizeBits = 4096;
    private static readonly TimeSpan RootValidity = TimeSpan.FromDays(365 * 10);
    private static readonly TimeSpan ServerLeafValidity = TimeSpan.FromDays(365 * 2);

    // Server Authentication / Client Authentication, per RFC 5280 appendix.
    private static readonly Oid ServerAuthEku = new("1.3.6.1.5.5.7.3.1");
    private static readonly Oid ClientAuthEku = new("1.3.6.1.5.5.7.3.2");

    // Process-wide, not per-instance: guards against two InternalCertificateAuthority
    // instances constructed concurrently in the same process (e.g. two
    // WebApplicationFactory-based test hosts sharing the default certs
    // directory) both seeing "no ca.pfx yet" and generating two different
    // roots, racing to write — which would leave whichever instance loses
    // the race holding an in-memory root that no longer matches the file
    // its own EnsureServerLeaf call reads moments later. Once ca.pfx exists,
    // every instance loads the same bytes and is trivially consistent; this
    // lock only closes the narrow "does it exist yet" race, not general
    // cross-process safety (this project's deployment model is one
    // container = one exclusive certs volume, where that doesn't apply).
    private static readonly Lock RootLock = new();

    private const string CurrentFileName = "ca.pfx";
    private const string PreviousFileName = "ca-previous.pfx";
    private const string PendingFileName = "ca-next.pfx";

    private readonly string _certsDirectory;
    private readonly Lock _issueLock = new();
    private readonly Lock _rotationLock = new();

    private X509Certificate2 _current;
    private X509Certificate2? _previous;
    private X509Certificate2? _pending;
    private X509Certificate2? _serverLeaf;
    private string? _serverLeafSanHostname;

    public InternalCertificateAuthority(string certsDirectory)
    {
        _certsDirectory = certsDirectory;
        Directory.CreateDirectory(_certsDirectory);
        _current = LoadOrCreateRoot();
        _previous = TryLoadRoot(PreviousFileName);
        _pending = TryLoadRoot(PendingFileName);

        TrustedRootCertificates = new X509Certificate2Collection();
        RebuildTrustedRoots();
    }

    public X509Certificate2 RootCertificate => _current;

    public X509Certificate2? PreviousRootCertificate => _previous;

    public X509Certificate2? PendingRootCertificate => _pending;

    public X509Certificate2Collection TrustedRootCertificates { get; }

    public X509Certificate2Collection AllKnownRootCertificates
    {
        get
        {
            var all = new X509Certificate2Collection { _current };
            if (_previous is not null)
            {
                all.Add(_previous);
            }

            if (_pending is not null)
            {
                all.Add(_pending);
            }

            return all;
        }
    }

    public X509Certificate2 CurrentServerLeaf =>
        _serverLeaf ?? throw new InvalidOperationException("EnsureServerLeaf must be called once at startup before CurrentServerLeaf is read.");

    public X509Certificate2 EnsureServerLeaf(string sanHostname)
    {
        lock (_rotationLock)
        {
            _serverLeafSanHostname = sanHostname;
            _serverLeaf = LoadOrCreateServerLeaf(sanHostname);
            return _serverLeaf;
        }
    }

    public IssuedCertificate IssueAgentLeaf(string hostname, TimeSpan validity)
    {
        lock (_issueLock)
        {
            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var notAfter = notBefore.Add(validity);
            var (cert, pfxBytes) = CreateLeaf(_current, hostname, [], ClientAuthEku, notBefore, notAfter);
            var thumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256);
            cert.Dispose();
            return new IssuedCertificate(pfxBytes, thumbprint, notBefore, notAfter);
        }
    }

    public CaRotationStatus GetRotationStatus() => new(
        CurrentThumbprint: _current.GetCertHashString(HashAlgorithmName.SHA256),
        CurrentNotAfter: _current.NotAfter,
        PreviousThumbprint: _previous?.GetCertHashString(HashAlgorithmName.SHA256),
        PreviousNotAfter: _previous?.NotAfter,
        PendingThumbprint: _pending?.GetCertHashString(HashAlgorithmName.SHA256),
        PendingNotAfter: _pending?.NotAfter);

    public X509Certificate2 PrepareRotation()
    {
        lock (_rotationLock)
        {
            var path = Path.Combine(_certsDirectory, PendingFileName);
            _pending = CreateAndPersistRoot(path);
            return _pending;
        }
    }

    public void ActivateRotation()
    {
        lock (_rotationLock)
        {
            if (_pending is null)
            {
                throw new InvalidOperationException("No pending root to activate — call PrepareRotation first.");
            }

            var currentPath = Path.Combine(_certsDirectory, CurrentFileName);
            var previousPath = Path.Combine(_certsDirectory, PreviousFileName);
            var pendingPath = Path.Combine(_certsDirectory, PendingFileName);

            // Ordered so a crash mid-way never leaves this CA without a
            // valid current root: the old current is preserved as previous
            // BEFORE ca.pfx is overwritten, and ca-next.pfx is only removed
            // AFTER the pending root has already taken over as current — a
            // process restart interrupted anywhere in this sequence still
            // finds a fully valid current root on disk, at worst re-reading
            // a rotation as still "prepared" (harmless — ActivateRotation
            // just needs calling again).
            File.Copy(currentPath, previousPath, overwrite: true);
            RestrictToOwner(previousPath);
            File.Copy(pendingPath, currentPath, overwrite: true);
            File.Delete(pendingPath);

            _previous = _current;
            _current = _pending;
            _pending = null;

            RebuildTrustedRoots();

            // The server's own leaf must switch to the new current root
            // immediately — an agent that already pre-trusted the pending
            // root (via GET /api/agent/ca-certificates on its own heartbeat
            // cadence) needs to see that trust rewarded right away, and one
            // that hasn't yet will simply keep retrying on its own poll
            // cadence, the same self-healing shape as every other
            // maintenance loop in this project.
            if (_serverLeafSanHostname is not null)
            {
                _serverLeaf = LoadOrCreateServerLeaf(_serverLeafSanHostname);
            }
        }
    }

    public void RetirePreviousRoot()
    {
        lock (_rotationLock)
        {
            if (_previous is null)
            {
                throw new InvalidOperationException("No previous root to retire.");
            }

            var previousPath = Path.Combine(_certsDirectory, PreviousFileName);
            if (File.Exists(previousPath))
            {
                File.Delete(previousPath);
            }

            _previous = null;
            RebuildTrustedRoots();
        }
    }

    private void RebuildTrustedRoots()
    {
        TrustedRootCertificates.Clear();
        TrustedRootCertificates.Add(_current);
        if (_previous is not null)
        {
            TrustedRootCertificates.Add(_previous);
        }
    }

    private X509Certificate2 LoadOrCreateServerLeaf(string sanHostname)
    {
        var path = Path.Combine(_certsDirectory, "server.pfx");
        if (File.Exists(path))
        {
            var existing = X509CertificateLoader.LoadPkcs12FromFile(path, password: null, X509KeyStorageFlags.Exportable);
            if (HasSan(existing, sanHostname) && ChainsTo(existing, _current))
            {
                return existing;
            }

            // Either the configured hostname changed since this leaf was
            // issued, or (updatewatch2-server#6) the current root has
            // rotated since — regenerate rather than fail; see the
            // class-level remarks on why this is always safe.
            existing.Dispose();
        }

        var (cert, pfxBytes) = CreateLeaf(_current, sanHostname, [sanHostname], ServerAuthEku, DateTimeOffset.UtcNow.AddMinutes(-5), null, ServerLeafValidity);
        File.WriteAllBytes(path, pfxBytes);
        RestrictToOwner(path);
        return cert;
    }

    private X509Certificate2? TryLoadRoot(string fileName)
    {
        var path = Path.Combine(_certsDirectory, fileName);
        return File.Exists(path) ? X509CertificateLoader.LoadPkcs12FromFile(path, password: null, X509KeyStorageFlags.Exportable) : null;
    }

    private X509Certificate2 LoadOrCreateRoot()
    {
        lock (RootLock)
        {
            var path = Path.Combine(_certsDirectory, CurrentFileName);
            if (File.Exists(path))
            {
                return X509CertificateLoader.LoadPkcs12FromFile(path, password: null, X509KeyStorageFlags.Exportable);
            }

            return CreateAndPersistRoot(path);
        }
    }

    private static X509Certificate2 CreateAndPersistRoot(string path)
    {
        using var rsa = RSA.Create(RootKeySizeBits);

        // The generation suffix matters, not just for an operator telling
        // two roots apart at a glance: X509Chain.Build() matches a leaf's
        // Issuer field against a candidate root's Subject by DN first —
        // two roots sharing byte-identical Subject text ("certificate
        // signature failure" was the actual live symptom this caused
        // before the suffix was added, i.e. NOT a theoretical concern) can
        // make chain building pick the wrong candidate to verify a
        // signature against. The X509AuthorityKeyIdentifierExtension added
        // to every leaf below (updatewatch2-server#6) is the standards-
        // compliant fix for that disambiguation; a unique Subject per root
        // generation is belt-and-suspenders on top of it, not a substitute.
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN=UpdateWatch2 Internal CA {DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}"),
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

    private static (X509Certificate2 Cert, byte[] PfxBytes) CreateLeaf(
        X509Certificate2 issuer, string subjectCn, IReadOnlyList<string> sanDnsNames, Oid enhancedKeyUsage,
        DateTimeOffset notBefore, DateTimeOffset? notAfterOverride, TimeSpan? validityIfNoOverride = null)
    {
        var notAfter = notAfterOverride ?? notBefore.Add(validityIfNoOverride!.Value);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(new X500DistinguishedName($"CN={subjectCn}"), ecdsa, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([enhancedKeyUsage], critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // Binds this leaf to the specific issuing root's key, not just its
        // Subject name — required once more than one root can be live at
        // once (updatewatch2-server#6): X509Chain.Build() otherwise matches
        // a candidate issuer by Subject DN first, and two roots (a rotated
        // current + a still-trusted previous) sharing a look-alike Subject
        // could make it try to verify this leaf's signature against the
        // wrong one. Confirmed live, not theoretical — this was missing
        // originally (fine with only ever one root in existence) and
        // reproduced a real "certificate signature failure" the moment a
        // second root existed, before this extension was added.
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
            issuer, includeKeyIdentifier: true, includeIssuerAndSerial: false));

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
        using var issuerKey = issuer.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Root CA certificate has no RSA private key to sign with.");
        var signatureGenerator = X509SignatureGenerator.CreateForRSA(issuerKey, RSASignaturePadding.Pkcs1);
        using var publicOnly = request.Create(issuer.SubjectName, signatureGenerator, notBefore, notAfter, serialNumber);
        using var withKey = publicOnly.CopyWithPrivateKey(ecdsa);

        var pfxBytes = withKey.Export(X509ContentType.Pfx);
        var reloaded = X509CertificateLoader.LoadPkcs12(pfxBytes, password: null, X509KeyStorageFlags.Exportable);
        return (reloaded, pfxBytes);
    }

    private static bool HasSan(X509Certificate2 cert, string hostname) =>
        cert.Extensions.OfType<X509SubjectAlternativeNameExtension>()
            .Any(ext => ext.EnumerateDnsNames().Contains(hostname, StringComparer.OrdinalIgnoreCase));

    private static bool ChainsTo(X509Certificate2 leaf, X509Certificate2 root)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(leaf);
    }

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
