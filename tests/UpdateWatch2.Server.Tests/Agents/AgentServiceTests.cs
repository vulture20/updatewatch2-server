using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;

namespace UpdateWatch2.Server.Tests.Agents;

public class AgentServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-agent-service-test-{Guid.NewGuid()}.sqlite");
    private readonly string _certsDirectory = Path.Combine(Path.GetTempPath(), $"uw2-agent-service-certs-{Guid.NewGuid()}");
    private readonly AppDbContext _db;
    private readonly AgentService _service;
    private readonly AgentRegistrationService _registrationService;

    private static readonly AgentRegisterRequest BareRequest = new(null, null, null, null, null, null);

    public AgentServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();

        var auditLog = new AuditLogService(_db);
        _service = new AgentService(_db, auditLog);
        var ca = new InternalCertificateAuthority(_certsDirectory);
        _registrationService = new AgentRegistrationService(_db, ca, auditLog);
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

    private async Task<string> RegisterApproveAndCertifyAsync(string hostname)
    {
        var registered = await _registrationService.RegisterAsync(hostname, BareRequest);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        agent.Approved = true;
        await _db.SaveChangesAsync();
        await _registrationService.RegisterAsync(hostname, BareRequest with { RegistrationToken = registered.RegistrationToken });
        return hostname;
    }

    [Fact]
    public async Task ReissueCertificateAsync_clears_certificate_fields_and_returns_a_fresh_verifiable_token()
    {
        var hostname = await RegisterApproveAndCertifyAsync("reissue-host");

        var result = await _service.ReissueCertificateAsync(hostname, initiatedBy: "admin");

        Assert.True(result.Success);
        Assert.NotNull(result.RegistrationToken);

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        Assert.Null(agent.ClientCertificateThumbprint);
        Assert.Null(agent.ClientCertificateIssuedAt);
        Assert.Null(agent.ClientCertificateExpiresAt);
        Assert.True(agent.Approved); // no re-approval needed
        Assert.NotNull(agent.RegistrationTokenHash);
        Assert.True(RegistrationTokenHasher.Verify(result.RegistrationToken!, agent.RegistrationTokenHash!));
    }

    [Fact]
    public async Task ReissueCertificateAsync_fails_for_an_unapproved_agent()
    {
        await _registrationService.RegisterAsync("never-approved", BareRequest);

        var result = await _service.ReissueCertificateAsync("never-approved", initiatedBy: "admin");

        Assert.False(result.Success);
        Assert.Null(result.RegistrationToken);
    }

    [Fact]
    public async Task ReissueCertificateAsync_fails_for_an_unknown_hostname()
    {
        var result = await _service.ReissueCertificateAsync("does-not-exist", initiatedBy: "admin");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ReissueCertificateAsync_writes_an_audit_log_entry_with_the_initiating_admin_as_actor()
    {
        var hostname = await RegisterApproveAndCertifyAsync("audited-host");

        await _service.ReissueCertificateAsync(hostname, initiatedBy: "alice");

        var entry = await _db.AuditLogEntries.SingleAsync(e => e.Action == "agent.certificate.reissue" && e.Details == hostname);
        Assert.Equal("alice", entry.Actor);
    }

    [Fact]
    public async Task Reissued_token_round_trips_through_RegisterAsync_and_yields_a_new_certificate()
    {
        var hostname = await RegisterApproveAndCertifyAsync("composed-host");
        var beforeReissue = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        var originalThumbprint = beforeReissue.ClientCertificateThumbprint;

        var reissue = await _service.ReissueCertificateAsync(hostname, initiatedBy: "admin");

        var outcome = await _registrationService.RegisterAsync(hostname, BareRequest with { RegistrationToken = reissue.RegistrationToken });

        Assert.Equal(AgentRegistrationStatus.Approved, outcome.Status);
        Assert.NotNull(outcome.CertificatePfxBase64);

        var afterReissue = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        Assert.NotNull(afterReissue.ClientCertificateThumbprint);
        Assert.NotEqual(originalThumbprint, afterReissue.ClientCertificateThumbprint);
    }
}
