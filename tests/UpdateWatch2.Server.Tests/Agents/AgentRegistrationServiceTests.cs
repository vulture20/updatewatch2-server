using Microsoft.EntityFrameworkCore;
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
    private readonly AgentRegistrationService _service;
    private readonly FakeAdminSettingsStore _settingsStore = new();

    private static readonly AgentRegisterRequest BareRequest = new(null, null, null, null, null, null);

    public AgentRegistrationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();

        var ca = new InternalCertificateAuthority(_certsDirectory);
        _service = new AgentRegistrationService(_db, ca, new AuditLogService(_db), _settingsStore);
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
    public async Task RecordAliveAsync_updates_LastAliveAt_for_a_known_agent_and_returns_false_for_an_unknown_one()
    {
        await _service.RegisterAsync("alive-host", BareRequest);

        var found = await _service.RecordAliveAsync("alive-host");
        var notFound = await _service.RecordAliveAsync("no-such-host");

        Assert.True(found);
        Assert.False(notFound);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "alive-host");
        Assert.NotNull(agent.LastAliveAt);
    }
}
