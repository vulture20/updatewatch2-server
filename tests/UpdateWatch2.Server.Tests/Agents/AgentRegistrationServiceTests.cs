using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.AgentUpdates;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Agents;

public class AgentRegistrationServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-agent-registration-test-{Guid.NewGuid()}.sqlite");
    private readonly string _certsDirectory = Path.Combine(Path.GetTempPath(), $"uw2-agent-registration-certs-{Guid.NewGuid()}");
    private readonly AppDbContext _db;
    private readonly InternalCertificateAuthority _ca;
    private readonly AgentRegistrationService _service;
    private readonly FakeAdminSettingsStore _settingsStore = new();
    private readonly FakeAgentUpdateService _agentUpdateService = new();

    private static readonly AgentRegisterRequest BareRequest = new(null, null, null, null, null, null);

    public AgentRegistrationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();

        _ca = new InternalCertificateAuthority(_certsDirectory);
        _service = new AgentRegistrationService(_db, _ca, new AuditLogService(_db), _settingsStore, _agentUpdateService);
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
    public async Task No_token_and_unknown_hostname_creates_the_agent_and_returns_a_pending_token()
    {
        var outcome = await _service.RegisterAsync("fresh-host", BareRequest);

        Assert.Equal(AgentRegistrationStatus.Pending, outcome.Status);
        Assert.NotNull(outcome.RegistrationToken);

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "fresh-host");
        Assert.False(agent.Approved);
        Assert.NotNull(agent.RegistrationTokenHash);
    }

    [Fact]
    public async Task No_token_for_an_already_registered_hostname_is_rejected()
    {
        await _service.RegisterAsync("claimed-host", BareRequest);

        var outcome = await _service.RegisterAsync("claimed-host", BareRequest);

        Assert.Equal(AgentRegistrationStatus.Rejected, outcome.Status);
    }

    [Fact]
    public async Task A_mismatched_token_is_rejected()
    {
        await _service.RegisterAsync("token-host", BareRequest);

        var outcome = await _service.RegisterAsync("token-host", BareRequest with { RegistrationToken = "not-the-real-token" });

        Assert.Equal(AgentRegistrationStatus.Rejected, outcome.Status);
    }

    [Fact]
    public async Task A_token_for_an_unknown_hostname_is_rejected()
    {
        var outcome = await _service.RegisterAsync("never-registered", BareRequest with { RegistrationToken = "whatever" });

        Assert.Equal(AgentRegistrationStatus.Rejected, outcome.Status);
    }

    [Fact]
    public async Task Correct_token_before_approval_is_idempotently_pending_with_no_new_token()
    {
        var first = await _service.RegisterAsync("waiting-host", BareRequest);

        var second = await _service.RegisterAsync("waiting-host", BareRequest with { RegistrationToken = first.RegistrationToken });

        Assert.Equal(AgentRegistrationStatus.Pending, second.Status);
        Assert.Null(second.RegistrationToken);
    }

    [Fact]
    public async Task Correct_token_after_approval_issues_a_certificate_exactly_once()
    {
        var registered = await _service.RegisterAsync("approved-host", BareRequest);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "approved-host");
        agent.Approved = true;
        await _db.SaveChangesAsync();

        var firstPoll = await _service.RegisterAsync("approved-host", BareRequest with { RegistrationToken = registered.RegistrationToken });
        Assert.Equal(AgentRegistrationStatus.Approved, firstPoll.Status);
        Assert.NotNull(firstPoll.CertificatePfxBase64);

        var reloaded = await _db.Agents.SingleAsync(a => a.Hostname == "approved-host");
        Assert.NotNull(reloaded.ClientCertificateThumbprint);
        Assert.NotNull(reloaded.ClientCertificateIssuedAt);
        Assert.NotNull(reloaded.ClientCertificateExpiresAt);
        Assert.Equal(_ca.RootCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), reloaded.IssuingRootThumbprint);
        Assert.Null(reloaded.RegistrationTokenHash); // cleared — no longer needed once delivered

        // Re-polling with the same (now-stale, no-longer-persisted) token:
        // still recognized as approved, but the certificate is never handed
        // out a second time.
        var secondPoll = await _service.RegisterAsync("approved-host", BareRequest with { RegistrationToken = registered.RegistrationToken });
        Assert.Equal(AgentRegistrationStatus.Approved, secondPoll.Status);
        Assert.Null(secondPoll.CertificatePfxBase64);

        // Even a wrong (or absent) token reaches the same steady state once
        // delivery has already happened — nothing sensitive is disclosed by
        // confirming "already approved" a second time, since the
        // certificate itself is never re-issued regardless.
        var wrongTokenPoll = await _service.RegisterAsync("approved-host", BareRequest with { RegistrationToken = "totally-wrong" });
        Assert.Equal(AgentRegistrationStatus.Approved, wrongTokenPoll.Status);
        Assert.Null(wrongTokenPoll.CertificatePfxBase64);
    }

    [Fact]
    public async Task RenewCertificateAsync_issues_a_new_certificate_and_updates_the_thumbprint()
    {
        var registered = await _service.RegisterAsync("renew-host", BareRequest);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "renew-host");
        agent.Approved = true;
        await _db.SaveChangesAsync();
        await _service.RegisterAsync("renew-host", BareRequest with { RegistrationToken = registered.RegistrationToken });
        var beforeRenewal = await _db.Agents.SingleAsync(a => a.Hostname == "renew-host");
        // Snapshot the values, not the tracked entity reference — _db's
        // change tracker returns the SAME instance on a later query, so
        // capturing beforeRenewal here and re-reading it after
        // RenewCertificateAsync (which mutates that same tracked instance)
        // would silently compare a value against itself.
        var originalThumbprint = beforeRenewal.ClientCertificateThumbprint;
        var originalIssuedAt = beforeRenewal.ClientCertificateIssuedAt;
        var originalExpiresAt = beforeRenewal.ClientCertificateExpiresAt;

        var result = await _service.RenewCertificateAsync("renew-host");

        Assert.True(result.Success);
        Assert.NotNull(result.CertificatePfxBase64);

        var afterRenewal = await _db.Agents.SingleAsync(a => a.Hostname == "renew-host");
        Assert.NotEqual(originalThumbprint, afterRenewal.ClientCertificateThumbprint);
        Assert.NotEqual(originalIssuedAt, afterRenewal.ClientCertificateIssuedAt);
        Assert.NotEqual(originalExpiresAt, afterRenewal.ClientCertificateExpiresAt);
        Assert.Equal(_ca.RootCertificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), afterRenewal.IssuingRootThumbprint);
    }

    [Fact]
    public async Task RenewCertificateAsync_fails_for_an_unknown_hostname()
    {
        var result = await _service.RenewCertificateAsync("does-not-exist");

        Assert.False(result.Success);
        Assert.Null(result.CertificatePfxBase64);
    }

    [Fact]
    public async Task RenewCertificateAsync_fails_for_an_agent_with_no_certificate_yet()
    {
        await _service.RegisterAsync("not-yet-certified", BareRequest);

        var result = await _service.RenewCertificateAsync("not-yet-certified");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task RecordAliveAsync_updates_LastAliveAt_for_a_known_agent_and_returns_null_for_an_unknown_one()
    {
        await _service.RegisterAsync("alive-host", BareRequest);

        var found = await _service.RecordAliveAsync("alive-host", request: null);
        var notFound = await _service.RecordAliveAsync("no-such-host", request: null);

        Assert.NotNull(found);
        Assert.False(found.InstallRequested);
        Assert.Null(found.UpdateAvailable);
        Assert.False(found.CertificateRotationPending);
        Assert.Null(notFound);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "alive-host");
        Assert.NotNull(agent.LastAliveAt);
    }

    [Fact]
    public async Task RecordAliveAsync_surfaces_whatever_IAgentUpdateService_offers_for_this_agents_reported_version()
    {
        await _service.RegisterAsync("update-offer-host", BareRequest with { AgentVersion = "0.9.0" });
        var expectedOffer = new AgentUpdateOffer("0.11.0", null, null, null);
        _agentUpdateService.Offer = expectedOffer;

        var result = await _service.RecordAliveAsync("update-offer-host", request: null);

        Assert.Same(expectedOffer, result!.UpdateAvailable);
    }

    [Fact]
    public async Task RecordAliveAsync_reports_InstallRequested_once_an_install_has_been_triggered()
    {
        await _service.RegisterAsync("install-pending-host", BareRequest);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "install-pending-host");
        agent.PendingInstallRequestedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var result = await _service.RecordAliveAsync("install-pending-host", request: null);

        Assert.NotNull(result);
        Assert.True(result.InstallRequested);
    }

    [Fact]
    public async Task RecordAliveAsync_reports_CertificateRotationPending_once_this_agents_leaf_is_signed_under_a_superseded_root()
    {
        // updatewatch2-server#6 follow-up: CA rotation never reissues an
        // already-onboarded agent's own leaf on its own — this is what lets
        // the agent notice and renew eagerly instead of waiting on its own
        // expiry-driven schedule.
        var registered = await _service.RegisterAsync("rotation-pending-host", BareRequest);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "rotation-pending-host");
        agent.Approved = true;
        await _db.SaveChangesAsync();
        await _service.RegisterAsync("rotation-pending-host", BareRequest with { RegistrationToken = registered.RegistrationToken });

        var beforeRotation = await _service.RecordAliveAsync("rotation-pending-host", request: null);
        Assert.False(beforeRotation!.CertificateRotationPending);

        _ca.PrepareRotation();
        _ca.ActivateRotation();

        var afterRotation = await _service.RecordAliveAsync("rotation-pending-host", request: null);
        Assert.True(afterRotation!.CertificateRotationPending);
    }

    [Fact]
    public async Task RecordAliveAsync_self_corrects_CertificateRotationPending_once_the_agent_has_renewed()
    {
        var registered = await _service.RegisterAsync("rotation-then-renewed-host", BareRequest);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "rotation-then-renewed-host");
        agent.Approved = true;
        await _db.SaveChangesAsync();
        await _service.RegisterAsync("rotation-then-renewed-host", BareRequest with { RegistrationToken = registered.RegistrationToken });

        _ca.PrepareRotation();
        _ca.ActivateRotation();
        Assert.True((await _service.RecordAliveAsync("rotation-then-renewed-host", request: null))!.CertificateRotationPending);

        await _service.RenewCertificateAsync("rotation-then-renewed-host");

        var afterRenewal = await _service.RecordAliveAsync("rotation-then-renewed-host", request: null);
        Assert.False(afterRenewal!.CertificateRotationPending);
    }

    [Fact]
    public async Task RecordAliveAsync_does_not_report_CertificateRotationPending_for_an_agent_with_no_known_issuing_root()
    {
        // A certificate issued before Agent.IssuingRootThumbprint existed
        // (or an agent with no certificate at all) must never be flagged —
        // "unknown" is a separate bucket from "confirmed still on the old
        // root" (see CaRotationImpactDto), not folded into a false positive.
        await _service.RegisterAsync("unknown-root-host", BareRequest);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "unknown-root-host");
        agent.ClientCertificateThumbprint = "simulated-pre-existing-thumbprint";
        agent.IssuingRootThumbprint = null;
        await _db.SaveChangesAsync();

        _ca.PrepareRotation();
        _ca.ActivateRotation();

        var result = await _service.RecordAliveAsync("unknown-root-host", request: null);

        Assert.False(result!.CertificateRotationPending);
    }

    [Fact]
    public async Task RecordAliveAsync_refreshes_self_reported_metadata_from_the_heartbeat_body()
    {
        // The gap updatewatch2-agent#6 closes: RegisterAsync's early-return
        // for an already-certified agent means registration itself never
        // runs again to catch a later change (a DHCP lease renewal, an OS
        // upgrade, a hostname change, an agent binary upgrade) — the alive
        // heartbeat is the only remaining channel.
        await _service.RegisterAsync("stale-metadata-host",
            new AgentRegisterRequest("old-dns", "Windows Server 2019", "10.0.0.1", "0.6.2", null, null));

        await _service.RecordAliveAsync("stale-metadata-host",
            new AgentAliveRequest("new-dns", "Windows Server 2025 Standard (Microsoft Windows 10.0.26100)", "10.0.0.2", "0.7.0"));

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "stale-metadata-host");
        Assert.Equal("new-dns", agent.DnsName);
        Assert.Equal("Windows Server 2025 Standard (Microsoft Windows 10.0.26100)", agent.OperatingSystem);
        Assert.Equal("10.0.0.2", agent.IpAddress);
        Assert.Equal("0.7.0", agent.AgentVersion);
    }

    [Fact]
    public async Task RecordAliveAsync_leaves_existing_metadata_untouched_when_a_field_is_absent_from_the_heartbeat()
    {
        await _service.RegisterAsync("partial-metadata-host",
            new AgentRegisterRequest("kept-dns", "Windows 11 24H2", "10.0.0.1", "0.6.2", null, null));

        // A field left null in the heartbeat body must not blank out a
        // previously known value — same "??" fallback RegisterAsync's own
        // poll-time refresh already relies on.
        await _service.RecordAliveAsync("partial-metadata-host",
            new AgentAliveRequest(DnsName: null, OperatingSystem: null, IpAddress: "10.0.0.2", AgentVersion: null));

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "partial-metadata-host");
        Assert.Equal("kept-dns", agent.DnsName);
        Assert.Equal("Windows 11 24H2", agent.OperatingSystem);
        Assert.Equal("10.0.0.2", agent.IpAddress);
        Assert.Equal("0.6.2", agent.AgentVersion);
    }
}
