using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Tests.Certificates;

public class CertificateValidatorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-cert-validator-test-{Guid.NewGuid()}.sqlite");
    private readonly string _certsDirectory = Path.Combine(Path.GetTempPath(), $"uw2-cert-validator-certs-{Guid.NewGuid()}");
    private readonly AppDbContext _db;
    private readonly InternalCertificateAuthority _ca;

    public CertificateValidatorTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();

        // A throwaway CA generated for this test run, exactly like the
        // AuthTestHelper pattern of seeding known state directly rather than
        // relying on files on disk — see InternalCertificateAuthorityTests
        // for the CA's own correctness tests; here it's just a source of
        // realistic certs to validate against.
        _ca = new InternalCertificateAuthority(_certsDirectory);
    }

    public void Dispose()
    {
        _db.Dispose();
        File.Delete(_dbPath);
        if (Directory.Exists(_certsDirectory))
        {
            Directory.Delete(_certsDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_a_certificate_from_an_unknown_thumbprint()
    {
        var validator = new CertificateValidator(_db);
        var issued = _ca.IssueAgentLeaf("never-registered-host");
        using var cert = X509CertificateLoader.LoadPkcs12(issued.PfxBytes, password: null);

        var result = await validator.ValidateAsync(cert);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Rejects_a_known_but_unapproved_agent()
    {
        var issued = _ca.IssueAgentLeaf("pending-host");
        _db.Agents.Add(new Agent { Hostname = "pending-host", Approved = false, ClientCertificateThumbprint = issued.ThumbprintSha256 });
        await _db.SaveChangesAsync();
        var validator = new CertificateValidator(_db);
        using var cert = X509CertificateLoader.LoadPkcs12(issued.PfxBytes, password: null);

        var result = await validator.ValidateAsync(cert);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Accepts_a_known_and_approved_agent_and_returns_its_hostname()
    {
        var issued = _ca.IssueAgentLeaf("approved-host");
        _db.Agents.Add(new Agent { Hostname = "approved-host", Approved = true, ClientCertificateThumbprint = issued.ThumbprintSha256 });
        await _db.SaveChangesAsync();
        var validator = new CertificateValidator(_db);
        using var cert = X509CertificateLoader.LoadPkcs12(issued.PfxBytes, password: null);

        var result = await validator.ValidateAsync(cert);

        Assert.True(result.Success);
        Assert.Equal("approved-host", result.Hostname);
    }

    [Fact]
    public async Task Thumbprint_comparison_uses_SHA256_not_the_legacy_SHA1_Thumbprint_property()
    {
        // Regression guard: if ValidateAsync or IssueAgentLeaf ever regress to
        // using X509Certificate2.Thumbprint (SHA-1) on one side and
        // GetCertHashString(SHA256) on the other, they'd silently never
        // match. This asserts the two are actually different for the same
        // cert (proving SHA-1 vs SHA-256 really do diverge here) and that
        // ValidateAsync still succeeds using the SHA-256 value end to end.
        var issued = _ca.IssueAgentLeaf("sha-check-host");
        using var cert = X509CertificateLoader.LoadPkcs12(issued.PfxBytes, password: null);
        Assert.NotEqual(cert.Thumbprint, cert.GetCertHashString(HashAlgorithmName.SHA256));

        _db.Agents.Add(new Agent { Hostname = "sha-check-host", Approved = true, ClientCertificateThumbprint = issued.ThumbprintSha256 });
        await _db.SaveChangesAsync();
        var validator = new CertificateValidator(_db);

        var result = await validator.ValidateAsync(cert);

        Assert.True(result.Success);
    }
}
