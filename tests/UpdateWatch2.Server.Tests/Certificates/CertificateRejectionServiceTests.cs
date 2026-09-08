using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Tests.Certificates;

public class CertificateRejectionServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-cert-rejection-test-{Guid.NewGuid()}.sqlite");
    private readonly string _certsDirectory = Path.Combine(Path.GetTempPath(), $"uw2-cert-rejection-certs-{Guid.NewGuid()}");
    private readonly AppDbContext _db;
    private readonly InternalCertificateAuthority _ca;
    private readonly CertificateRejectionService _service;

    public CertificateRejectionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();
        _ca = new InternalCertificateAuthority(_certsDirectory);
        _service = new CertificateRejectionService(_db, new AuditLogService(_db), NullLogger<CertificateRejectionService>.Instance);
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
    public async Task RecordAsync_writes_an_audit_log_entry_encoding_the_reason_in_the_action()
    {
        var issued = _ca.IssueAgentLeaf("rejected-host", TimeSpan.FromDays(730));
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(issued.PfxBytes, password: null);

        await _service.RecordAsync(CertificateRejectionReason.UnknownAgent, cert, "203.0.113.5");

        var entry = await _db.AuditLogEntries.SingleAsync();
        Assert.Equal("agent.certificate.rejected.UnknownAgent", entry.Action);
        Assert.Equal(issued.ThumbprintSha256, entry.Actor);
        Assert.Contains("203.0.113.5", entry.Details);
    }

    [Fact]
    public async Task RecordAsync_uses_unknown_as_the_actor_when_no_certificate_is_available()
    {
        await _service.RecordAsync(CertificateRejectionReason.NotTrusted, certificate: null, remoteIpAddress: null);

        var entry = await _db.AuditLogEntries.SingleAsync();
        Assert.Equal("unknown", entry.Actor);
    }

    [Fact]
    public async Task GetStatusAsync_counts_recent_rejections_and_returns_their_reasons()
    {
        await _service.RecordAsync(CertificateRejectionReason.Expired, certificate: null, remoteIpAddress: null);
        await _service.RecordAsync(CertificateRejectionReason.NotTrusted, certificate: null, remoteIpAddress: null);

        var status = await _service.GetStatusAsync();

        Assert.Equal(2, status.RecentCount);
        Assert.Equal(2, status.Recent.Count);
        Assert.Contains(status.Recent, r => r.Reason == "Expired");
        Assert.Contains(status.Recent, r => r.Reason == "NotTrusted");
    }

    [Fact]
    public async Task GetStatusAsync_excludes_rejections_older_than_the_lookback_window()
    {
        _db.AuditLogEntries.Add(new AuditLogEntry
        {
            Actor = "unknown",
            Action = CertificateRejectionService.ActionPrefix + CertificateRejectionReason.Expired,
            Details = "old",
            Timestamp = DateTimeOffset.UtcNow.AddHours(-25),
        });
        await _db.SaveChangesAsync();

        var status = await _service.GetStatusAsync();

        Assert.Equal(0, status.RecentCount);
        Assert.Empty(status.Recent);
    }

    [Fact]
    public async Task GetStatusAsync_ignores_unrelated_audit_log_entries()
    {
        await _db.AuditLogEntries.AddAsync(new AuditLogEntry { Actor = "admin", Action = "agent.approve", Details = "some-host" });
        await _db.SaveChangesAsync();

        var status = await _service.GetStatusAsync();

        Assert.Equal(0, status.RecentCount);
    }

    [Fact]
    public async Task GetStatusAsync_returns_zero_with_no_rejections()
    {
        var status = await _service.GetStatusAsync();

        Assert.Equal(0, status.RecentCount);
        Assert.Empty(status.Recent);
    }
}
