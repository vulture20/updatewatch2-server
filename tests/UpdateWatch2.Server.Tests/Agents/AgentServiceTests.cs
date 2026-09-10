using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Agents;

public class AgentServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-agent-service-test-{Guid.NewGuid()}.sqlite");
    private readonly string _certsDirectory = Path.Combine(Path.GetTempPath(), $"uw2-agent-service-certs-{Guid.NewGuid()}");
    private readonly AppDbContext _db;
    private readonly AgentService _service;
    private readonly InternalCertificateAuthority _ca;
    private readonly AgentRegistrationService _registrationService;
    private readonly CertificateRejectionService _rejectionService;

    private static readonly AgentRegisterRequest BareRequest = new(null, null, null, null, null, null);

    public AgentServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();

        var auditLog = new AuditLogService(_db);
        _rejectionService = new CertificateRejectionService(_db, auditLog, NullLogger<CertificateRejectionService>.Instance);
        _service = new AgentService(_db, auditLog, _rejectionService);
        _ca = new InternalCertificateAuthority(_certsDirectory);
        _registrationService = new AgentRegistrationService(_db, _ca, auditLog, new FakeAdminSettingsStore(), new FakeAgentUpdateService());
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

    [Fact]
    public async Task DeleteAsync_removes_the_agent_and_cascades_its_update_items()
    {
        var hostname = await RegisterApproveAndCertifyAsync("delete-host");
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        _db.UpdateItems.Add(new UpdateItem { AgentId = agent.Id, Title = "Some Update" });
        await _db.SaveChangesAsync();

        var result = await _service.DeleteAsync(hostname, initiatedBy: "admin");

        Assert.True(result);
        Assert.False(await _db.Agents.AnyAsync(a => a.Hostname == hostname));
        Assert.False(await _db.UpdateItems.AnyAsync(u => u.AgentId == agent.Id));
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_an_unknown_hostname()
    {
        var result = await _service.DeleteAsync("does-not-exist", initiatedBy: "admin");

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_writes_an_audit_log_entry_with_the_initiating_admin_as_actor()
    {
        var hostname = await RegisterApproveAndCertifyAsync("audited-delete-host");

        await _service.DeleteAsync(hostname, initiatedBy: "alice");

        var entry = await _db.AuditLogEntries.SingleAsync(e => e.Action == "agent.delete" && e.Details == hostname);
        Assert.Equal("alice", entry.Actor);
    }

    [Fact]
    public async Task A_deleted_hostname_starts_over_as_a_brand_new_unapproved_agent_on_re_registration()
    {
        var hostname = await RegisterApproveAndCertifyAsync("re-registering-host");
        await _service.DeleteAsync(hostname, initiatedBy: "admin");

        var outcome = await _registrationService.RegisterAsync(hostname, BareRequest);

        Assert.Equal(AgentRegistrationStatus.Pending, outcome.Status);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        Assert.False(agent.Approved);
    }

    [Fact]
    public async Task GetCaRotationImpactAsync_returns_an_empty_summary_when_there_is_no_previous_root()
    {
        await RegisterApproveAndCertifyAsync("some-host");

        var impact = await _service.GetCaRotationImpactAsync(previousRootThumbprintSha256: null);

        Assert.Equal(0, impact.StillOnPreviousRootCount);
        Assert.Empty(impact.StillOnPreviousRootHostnames);
        Assert.Equal(0, impact.UnknownRootAgentCount);
    }

    [Fact]
    public async Task GetCaRotationImpactAsync_counts_and_lists_agents_still_on_the_previous_root_but_not_ones_already_renewed()
    {
        var stillOnOldRoot = await RegisterApproveAndCertifyAsync("still-on-old-root");
        var oldRootThumbprint = _ca.RootCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);

        _ca.PrepareRotation();
        _ca.ActivateRotation();
        await RegisterApproveAndCertifyAsync("already-on-new-root"); // issued after rotation

        var impact = await _service.GetCaRotationImpactAsync(oldRootThumbprint);

        Assert.Equal(1, impact.StillOnPreviousRootCount);
        Assert.Equal([stillOnOldRoot], impact.StillOnPreviousRootHostnames);
    }

    [Fact]
    public async Task GetCaRotationImpactAsync_counts_agents_with_no_recorded_issuing_root_separately_from_confirmed_ones()
    {
        // Simulates a certificate issued before Agent.IssuingRootThumbprint
        // existed — "can't verify" must never be silently folded into
        // "confirmed still on the old root" (a false positive) or dropped
        // entirely (hiding a real risk from the admin).
        await RegisterApproveAndCertifyAsync("pre-feature-host");
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "pre-feature-host");
        agent.IssuingRootThumbprint = null;
        await _db.SaveChangesAsync();
        var oldRootThumbprint = _ca.RootCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);

        _ca.PrepareRotation();
        _ca.ActivateRotation();

        var impact = await _service.GetCaRotationImpactAsync(oldRootThumbprint);

        Assert.Equal(0, impact.StillOnPreviousRootCount);
        Assert.Empty(impact.StillOnPreviousRootHostnames);
        Assert.Equal(1, impact.UnknownRootAgentCount);
    }

    [Fact]
    public async Task GetByHostnameAsync_surfaces_the_issuing_CA_root_thumbprint()
    {
        var hostname = await RegisterApproveAndCertifyAsync("ca-root-host");
        var rootThumbprint = _ca.RootCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);

        var detail = await _service.GetByHostnameAsync(hostname);

        Assert.Equal(rootThumbprint, detail!.IssuingRootThumbprint);
    }

    [Fact]
    public async Task GetByHostnameAsync_returns_a_null_issuing_root_for_an_agent_with_no_certificate()
    {
        await _registrationService.RegisterAsync("no-cert-host", BareRequest);

        var detail = await _service.GetByHostnameAsync("no-cert-host");

        Assert.Null(detail!.IssuingRootThumbprint);
    }

    [Fact]
    public async Task GetAllAsync_excludes_updates_matching_an_active_filter_from_the_pending_count()
    {
        var hostname = await RegisterApproveAndCertifyAsync("filtered-host");
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        _db.UpdateItems.AddRange(
            new UpdateItem { AgentId = agent.Id, Title = "Security Intelligence-Update für Microsoft Defender Antivirus" },
            new UpdateItem { AgentId = agent.Id, Title = "2026-08 Kumulatives Update für Windows 11" });
        _db.UpdateFilters.Add(new UpdateFilter { Name = "Defender", Pattern = "Security Intelligence-Update" });
        await _db.SaveChangesAsync();

        var list = await _service.GetAllAsync();

        var item = Assert.Single(list, a => a.Hostname == hostname);
        Assert.Equal(1, item.PendingUpdateCount);
    }

    [Fact]
    public async Task GetByHostnameAsync_excludes_updates_matching_an_active_filter_from_the_pending_count()
    {
        var hostname = await RegisterApproveAndCertifyAsync("filtered-detail-host");
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        _db.UpdateItems.AddRange(
            new UpdateItem { AgentId = agent.Id, Title = "Security Intelligence-Update für Microsoft Defender Antivirus" },
            new UpdateItem { AgentId = agent.Id, Title = "2026-08 Kumulatives Update für Windows 11" });
        _db.UpdateFilters.Add(new UpdateFilter { Name = "Defender", Pattern = "Security Intelligence-Update" });
        await _db.SaveChangesAsync();

        var detail = await _service.GetByHostnameAsync(hostname);

        Assert.Equal(1, detail!.PendingUpdateCount);
    }

    [Fact]
    public async Task Filtered_pending_count_updates_immediately_when_a_filter_is_added_no_re_report_needed()
    {
        var hostname = await RegisterApproveAndCertifyAsync("live-filter-host");
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        _db.UpdateItems.Add(new UpdateItem { AgentId = agent.Id, Title = "Some Update" });
        await _db.SaveChangesAsync();

        var before = await _service.GetByHostnameAsync(hostname);
        Assert.Equal(1, before!.PendingUpdateCount);

        _db.UpdateFilters.Add(new UpdateFilter { Name = "Catch-all", Pattern = "Some Update" });
        await _db.SaveChangesAsync();

        var after = await _service.GetByHostnameAsync(hostname);
        Assert.Equal(0, after!.PendingUpdateCount);
    }

    [Fact]
    public async Task GetAllAsync_flags_an_agent_that_recently_presented_a_rejected_certificate()
    {
        var hostname = await RegisterApproveAndCertifyAsync("cert-flagged-host");
        var reissued = _ca.IssueAgentLeaf(hostname, TimeSpan.FromDays(730));
        using var expiredLikeCert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(reissued.PfxBytes, password: null);
        await _rejectionService.RecordAsync(CertificateRejectionReason.NotTrusted, expiredLikeCert, remoteIpAddress: null);

        var list = await _service.GetAllAsync();

        var item = Assert.Single(list, a => a.Hostname == hostname);
        Assert.Equal("NotTrusted", item.LastCertificateRejectionReason);
    }

    [Fact]
    public async Task GetAllAsync_leaves_LastCertificateRejectionReason_null_with_no_recent_rejection()
    {
        var hostname = await RegisterApproveAndCertifyAsync("unflagged-host");

        var list = await _service.GetAllAsync();

        var item = Assert.Single(list, a => a.Hostname == hostname);
        Assert.Null(item.LastCertificateRejectionReason);
    }

    [Fact]
    public async Task GetAllAsync_surfaces_operating_system_and_last_alive_at_for_the_overview_list()
    {
        // These two fields (added so AgentsListPage can show a per-row OS
        // icon, an OS filter, and a "last seen" column without a per-agent
        // round trip) already exist on AgentDetailDto — this just confirms
        // GetAllAsync's own projection carries them too, not only the
        // detail lookup.
        var hostname = await RegisterApproveAndCertifyAsync("os-and-alive-host");
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        agent.OperatingSystem = "Ubuntu 22.04 LTS";
        agent.LastAliveAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var list = await _service.GetAllAsync();

        var item = Assert.Single(list, a => a.Hostname == hostname);
        Assert.Equal("Ubuntu 22.04 LTS", item.OperatingSystem);
        Assert.Equal(agent.LastAliveAt, item.LastAliveAt);
    }

    [Fact]
    public async Task GetByHostnameAsync_surfaces_the_rejection_reason_and_timestamp()
    {
        var hostname = await RegisterApproveAndCertifyAsync("cert-flagged-detail-host");
        var reissued = _ca.IssueAgentLeaf(hostname, TimeSpan.FromDays(730));
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(reissued.PfxBytes, password: null);
        await _rejectionService.RecordAsync(CertificateRejectionReason.UnknownAgent, cert, remoteIpAddress: null);

        var detail = await _service.GetByHostnameAsync(hostname);

        Assert.Equal("UnknownAgent", detail!.LastCertificateRejectionReason);
        Assert.NotNull(detail.LastCertificateRejectionAt);
    }

    [Fact]
    public async Task GetByHostnameAsync_clears_the_rejection_flag_once_the_agent_has_heartbeated_successfully_since()
    {
        // Regression guard for a real user report: fixing a certificate
        // problem left the warning icon showing for up to 24h afterward,
        // because only the rejection's own age (not whether the agent had
        // since proven itself healthy again) decided whether to flag it.
        var hostname = await RegisterApproveAndCertifyAsync("recovered-host");
        var reissued = _ca.IssueAgentLeaf(hostname, TimeSpan.FromDays(730));
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(reissued.PfxBytes, password: null);
        await _rejectionService.RecordAsync(CertificateRejectionReason.NotTrusted, cert, remoteIpAddress: null);

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        agent.LastAliveAt = DateTimeOffset.UtcNow; // a later successful heartbeat
        await _db.SaveChangesAsync();

        var detail = await _service.GetByHostnameAsync(hostname);

        Assert.Null(detail!.LastCertificateRejectionReason);
        Assert.Null(detail.LastCertificateRejectionAt);
    }

    [Fact]
    public async Task GetByHostnameAsync_keeps_the_flag_when_the_last_successful_heartbeat_predates_the_rejection()
    {
        var hostname = await RegisterApproveAndCertifyAsync("still-broken-host");
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        agent.LastAliveAt = DateTimeOffset.UtcNow.AddMinutes(-10); // healthy 10 minutes ago
        await _db.SaveChangesAsync();

        var reissued = _ca.IssueAgentLeaf(hostname, TimeSpan.FromDays(730));
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(reissued.PfxBytes, password: null);
        await _rejectionService.RecordAsync(CertificateRejectionReason.NotTrusted, cert, remoteIpAddress: null); // then broke just now

        var detail = await _service.GetByHostnameAsync(hostname);

        Assert.Equal("NotTrusted", detail!.LastCertificateRejectionReason);
    }

    [Fact]
    public async Task GetAllAsync_clears_the_rejection_flag_once_the_agent_has_heartbeated_successfully_since()
    {
        var hostname = await RegisterApproveAndCertifyAsync("recovered-list-host");
        var reissued = _ca.IssueAgentLeaf(hostname, TimeSpan.FromDays(730));
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(reissued.PfxBytes, password: null);
        await _rejectionService.RecordAsync(CertificateRejectionReason.NotTrusted, cert, remoteIpAddress: null);

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == hostname);
        agent.LastAliveAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var list = await _service.GetAllAsync();

        var item = Assert.Single(list, a => a.Hostname == hostname);
        Assert.Null(item.LastCertificateRejectionReason);
    }
}
