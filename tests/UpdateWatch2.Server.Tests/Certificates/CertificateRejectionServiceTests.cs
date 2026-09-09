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
        // The certificate's own CN ("rejected-host" — every agent leaf's
        // Subject is "CN=<hostname>"), not the thumbprint, so a rejection
        // can be attributed back to the agent it claims to be (see
        // GetRecentByHostnameAsync / AgentService's flagging).
        Assert.Equal("rejected-host", entry.Actor);
        Assert.Contains("203.0.113.5", entry.Details);
        Assert.Contains(issued.ThumbprintSha256, entry.Details);
    }

    [Fact]
    public async Task RecordAsync_uses_unknown_as_the_actor_when_no_certificate_or_ip_is_available()
    {
        await _service.RecordAsync(CertificateRejectionReason.NotTrusted, certificate: null, remoteIpAddress: null);

        var entry = await _db.AuditLogEntries.SingleAsync();
        Assert.Equal("unknown", entry.Actor);
    }

    [Fact]
    public async Task RecordAsync_falls_back_to_the_remote_ip_not_the_thumbprint_when_no_hostname_can_be_resolved()
    {
        // The fallback used to be the certificate's own thumbprint —
        // changed on request: a thumbprint means nothing to an admin
        // scanning the audit log, an IP is at least actionable.
        await _service.RecordAsync(CertificateRejectionReason.NotTrusted, certificate: null, remoteIpAddress: "203.0.113.9");

        var entry = await _db.AuditLogEntries.SingleAsync();
        Assert.Equal("203.0.113.9", entry.Actor);
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

    [Fact]
    public async Task AcknowledgeAsync_silences_the_banner_for_rejections_recorded_so_far()
    {
        await _service.RecordAsync(CertificateRejectionReason.Expired, certificate: null, remoteIpAddress: null);

        await _service.AcknowledgeAsync("alice");
        var status = await _service.GetStatusAsync();

        Assert.Equal(0, status.RecentCount);
        Assert.Empty(status.Recent);
    }

    [Fact]
    public async Task AcknowledgeAsync_does_not_silence_a_rejection_recorded_after_it()
    {
        await _service.RecordAsync(CertificateRejectionReason.Expired, certificate: null, remoteIpAddress: null);
        await _service.AcknowledgeAsync("alice");

        await _service.RecordAsync(CertificateRejectionReason.NotTrusted, certificate: null, remoteIpAddress: null);
        var status = await _service.GetStatusAsync();

        Assert.Equal(1, status.RecentCount);
        Assert.Equal("NotTrusted", status.Recent[0].Reason);
    }

    [Fact]
    public async Task AcknowledgeAsync_writes_an_audit_log_entry_with_the_acknowledging_admin_as_actor()
    {
        await _service.RecordAsync(CertificateRejectionReason.Expired, certificate: null, remoteIpAddress: null);

        await _service.AcknowledgeAsync("alice");

        var entry = await _db.AuditLogEntries.SingleAsync(e => e.Action == "certificate-rejections.acknowledge");
        Assert.Equal("alice", entry.Actor);
    }

    [Fact]
    public async Task AcknowledgeAsync_can_be_called_again_later_and_updates_who_acknowledged()
    {
        await _service.AcknowledgeAsync("alice");
        await _service.AcknowledgeAsync("bob");

        var row = await _db.CertificateRejectionAcknowledgements.SingleAsync();
        Assert.Equal("bob", row.AcknowledgedBy);
    }

    [Fact]
    public async Task AcknowledgeAsync_does_not_affect_GetRecentByHostnameAsync()
    {
        // Acknowledging the banner means "an admin has seen this", not "the
        // underlying agent's certificate problem is fixed" — the per-agent
        // warning icon (AgentService, via GetRecentByHostnameAsync) must
        // stay governed only by a later successful heartbeat.
        var issued = _ca.IssueAgentLeaf("still-flagged-host", TimeSpan.FromDays(730));
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(issued.PfxBytes, password: null);
        await _service.RecordAsync(CertificateRejectionReason.NotTrusted, cert, remoteIpAddress: null);

        await _service.AcknowledgeAsync("alice");

        var byHostname = await _service.GetRecentByHostnameAsync();
        Assert.Contains("still-flagged-host", byHostname.Keys);
    }

    [Fact]
    public async Task GetRecentByHostnameAsync_keys_by_the_certificates_own_CN()
    {
        var issued = _ca.IssueAgentLeaf("flagged-host", TimeSpan.FromDays(730));
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(issued.PfxBytes, password: null);
        await _service.RecordAsync(CertificateRejectionReason.AgentNotApproved, cert, remoteIpAddress: null);

        var byHostname = await _service.GetRecentByHostnameAsync();

        var entry = Assert.Single(byHostname);
        Assert.Equal("flagged-host", entry.Key);
        Assert.Equal("AgentNotApproved", entry.Value.Reason);
    }

    [Fact]
    public async Task GetRecentByHostnameAsync_keeps_only_the_most_recent_rejection_per_hostname()
    {
        var issued = _ca.IssueAgentLeaf("repeat-offender", TimeSpan.FromDays(730));
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(issued.PfxBytes, password: null);
        await _service.RecordAsync(CertificateRejectionReason.NotTrusted, cert, remoteIpAddress: null);
        await _service.RecordAsync(CertificateRejectionReason.Expired, cert, remoteIpAddress: null);

        var byHostname = await _service.GetRecentByHostnameAsync();

        var entry = Assert.Single(byHostname);
        Assert.Equal("Expired", entry.Value.Reason);
    }

    [Fact]
    public async Task GetRecentByHostnameAsync_returns_empty_with_no_rejections()
    {
        var byHostname = await _service.GetRecentByHostnameAsync();

        Assert.Empty(byHostname);
    }
}
