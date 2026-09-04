using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;

namespace UpdateWatch2.Server.Tests.Agents;

public class AgentRegistrationServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-agent-registration-test-{Guid.NewGuid()}.sqlite");
    private readonly string _certsDirectory = Path.Combine(Path.GetTempPath(), $"uw2-agent-registration-certs-{Guid.NewGuid()}");
    private readonly AppDbContext _db;
    private readonly AgentRegistrationService _service;

    private static readonly AgentRegisterRequest BareRequest = new(null, null, null, null, null, null);

    public AgentRegistrationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();

        var ca = new InternalCertificateAuthority(_certsDirectory);
        _service = new AgentRegistrationService(_db, ca, new AuditLogService(_db));
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
