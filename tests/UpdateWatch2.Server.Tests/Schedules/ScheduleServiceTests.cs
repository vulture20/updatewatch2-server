using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Api;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Notifications;
using UpdateWatch2.Server.Schedules;
using UpdateWatch2.Server.Tests.TestHelpers;
using UpdateWatch2.Server.Updates;

namespace UpdateWatch2.Server.Tests.Schedules;

/// <summary>
/// Covers the pure server-side orchestration this feature is built on
/// (updatewatch2-server#25) — firing a schedule delegates to the exact
/// same <see cref="IUpdateService"/>/<see cref="IAgentService"/> bulk
/// methods the admin UI's own bulk-action toolbar uses, so these tests
/// exercise real instances of both, not fakes, the same way
/// <c>AgentServiceTests</c> exercises a real <c>AgentRegistrationService</c>
/// alongside it.
/// </summary>
public class ScheduleServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-schedule-service-test-{Guid.NewGuid()}.sqlite");
    private readonly string _certsDirectory = Path.Combine(Path.GetTempPath(), $"uw2-schedule-service-certs-{Guid.NewGuid()}");
    private readonly AppDbContext _db;
    private readonly ScheduleService _service;
    private readonly AgentService _agentService;
    private readonly UpdateService _updateService;
    private readonly AgentRegistrationService _registrationService;
    private readonly FakeAdminSettingsStore _settingsStore = new(smtp: new SmtpOptions { Host = "smtp.example.com", FromAddress = "noreply@example.com", NotificationRecipientAddress = "admin@example.com" });
    private readonly FakeEmailNotificationService _email = new();

    public ScheduleServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();

        var auditLog = new AuditLogService(_db);
        var rejectionService = new CertificateRejectionService(_db, auditLog, NullLogger<CertificateRejectionService>.Instance);
        _agentService = new AgentService(_db, auditLog, rejectionService, _settingsStore, _email, NullLogger<AgentService>.Instance);
        _updateService = new UpdateService(_db, auditLog, _settingsStore, _email, NullLogger<UpdateService>.Instance);
        _registrationService = new AgentRegistrationService(_db, new InternalCertificateAuthority(_certsDirectory), auditLog, _settingsStore, new FakeAgentUpdateService());
        _service = new ScheduleService(_db, _updateService, _agentService, auditLog, _settingsStore, _email, NullLogger<ScheduleService>.Instance);
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

    private async Task<Agent> AddAgentAsync(string hostname, bool rebootRequired = false)
    {
        var agent = new Agent { Hostname = hostname, Approved = true, RebootRequired = rebootRequired };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();
        return agent;
    }

    private static UpsertScheduleRequest OnceRequest(IReadOnlyList<string> hostnames, DateTimeOffset onceAt, bool actionInstall = true, bool actionReboot = false, bool rebootOnlyIfRequired = false, bool notifyOnFailure = true) => new(
        Name: "test-schedule",
        Enabled: true,
        ScheduleType: ScheduleType.Once,
        Pattern: null,
        OnceAt: onceAt,
        WeeklyDays: null,
        TimeOfDay: default,
        IntervalDays: null,
        IntervalStartDate: null,
        CronExpression: null,
        ActionInstall: actionInstall,
        ActionReboot: actionReboot,
        RebootOnlyIfRequired: rebootOnlyIfRequired,
        DeadlineHours: 4,
        NotifyOnFailure: notifyOnFailure,
        Hostnames: hostnames);

    private static UpsertScheduleRequest CronRequest(IReadOnlyList<string> hostnames, string? cronExpression) => new(
        Name: "test-cron-schedule",
        Enabled: true,
        ScheduleType: ScheduleType.Cron,
        Pattern: null,
        OnceAt: null,
        WeeklyDays: null,
        TimeOfDay: default,
        IntervalDays: null,
        IntervalStartDate: null,
        CronExpression: cronExpression,
        ActionInstall: true,
        ActionReboot: false,
        RebootOnlyIfRequired: false,
        DeadlineHours: 4,
        NotifyOnFailure: true,
        Hostnames: hostnames);

    [Fact]
    public async Task CreateAsync_rejects_an_empty_name()
    {
        await AddAgentAsync("host-1");

        var result = await _service.CreateAsync(OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddHours(1)) with { Name = "" }, "admin");

        Assert.False(result.Success);
        Assert.Equal(ApiErrorCode.ScheduleNameRequired, result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_rejects_no_agents_selected()
    {
        var result = await _service.CreateAsync(OnceRequest([], DateTimeOffset.UtcNow.AddHours(1)), "admin");

        Assert.False(result.Success);
        Assert.Equal(ApiErrorCode.ScheduleAgentsRequired, result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_agent()
    {
        var result = await _service.CreateAsync(OnceRequest(["no-such-host"], DateTimeOffset.UtcNow.AddHours(1)), "admin");

        Assert.False(result.Success);
        Assert.Equal(ApiErrorCode.ScheduleUnknownAgents, result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_once_date_in_the_past()
    {
        await AddAgentAsync("host-1");

        var result = await _service.CreateAsync(OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddHours(-1)), "admin");

        Assert.False(result.Success);
        Assert.Equal(ApiErrorCode.ScheduleOnceAtInPast, result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_rejects_neither_action_selected()
    {
        await AddAgentAsync("host-1");

        var result = await _service.CreateAsync(OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddHours(1), actionInstall: false, actionReboot: false), "admin");

        Assert.False(result.Success);
        Assert.Equal(ApiErrorCode.ScheduleActionRequired, result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_computes_NextRunAt_from_OnceAt()
    {
        await AddAgentAsync("host-1");
        var onceAt = DateTimeOffset.UtcNow.AddDays(2);

        var result = await _service.CreateAsync(OnceRequest(["host-1"], onceAt), "admin");

        Assert.True(result.Success);
        Assert.Equal(onceAt, result.Schedule!.NextRunAt);
        Assert.Equal(ScheduleStatus.Active, result.Schedule.Status);
    }

    [Fact]
    public async Task CreateAsync_leaves_NextRunAt_null_while_disabled()
    {
        await AddAgentAsync("host-1");

        var result = await _service.CreateAsync(OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddDays(1)) with { Enabled = false }, "admin");

        Assert.True(result.Success);
        Assert.Null(result.Schedule!.NextRunAt);
        Assert.Equal(ScheduleStatus.Paused, result.Schedule.Status);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_cron_schedule_with_no_expression()
    {
        await AddAgentAsync("host-1");

        var result = await _service.CreateAsync(CronRequest(["host-1"], null), "admin");

        Assert.False(result.Success);
        Assert.Equal(ApiErrorCode.ScheduleCronExpressionRequired, result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unparseable_cron_expression()
    {
        await AddAgentAsync("host-1");

        var result = await _service.CreateAsync(CronRequest(["host-1"], "not a cron expression"), "admin");

        Assert.False(result.Success);
        Assert.Equal(ApiErrorCode.ScheduleCronExpressionInvalid, result.ErrorCode);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task CreateAsync_accepts_a_valid_cron_expression_and_computes_NextRunAt()
    {
        await AddAgentAsync("host-1");

        // Every minute — guarantees a next occurrence within 60s of "now"
        // regardless of when this test happens to run.
        var result = await _service.CreateAsync(CronRequest(["host-1"], "* * * * *"), "admin");

        Assert.True(result.Success);
        Assert.Equal("* * * * *", result.Schedule!.CronExpression);
        Assert.NotNull(result.Schedule.NextRunAt);
        Assert.True(result.Schedule.NextRunAt <= DateTimeOffset.UtcNow.AddMinutes(1).AddSeconds(1));
    }

    [Fact]
    public async Task UpdateAsync_replaces_agent_membership()
    {
        await AddAgentAsync("host-1");
        await AddAgentAsync("host-2");
        var created = await _service.CreateAsync(OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddDays(1)), "admin");

        var updated = await _service.UpdateAsync(created.Schedule!.Id, OnceRequest(["host-2"], DateTimeOffset.UtcNow.AddDays(1)), "admin");

        Assert.True(updated.Success);
        Assert.Equal(["host-2"], updated.Schedule!.Hostnames);
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_an_unknown_id()
    {
        Assert.False(await _service.DeleteAsync(9999, "admin"));
    }

    [Fact]
    public async Task DeleteAsync_cancels_a_still_pending_install_from_this_schedules_last_run()
    {
        await AddAgentAsync("host-1");
        var created = await _service.CreateAsync(OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1)), "admin");
        await _service.RunNowAsync(created.Schedule!.Id, "admin");

        var agentBeforeDelete = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.NotNull(agentBeforeDelete.PendingInstallRequestedAt);
        Assert.NotNull(agentBeforeDelete.PendingInstallScheduleRunId);

        await _service.DeleteAsync(created.Schedule.Id, "admin");

        var agentAfterDelete = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.Null(agentAfterDelete.PendingInstallRequestedAt);
        Assert.Null(agentAfterDelete.PendingInstallScheduleRunId);
    }

    [Fact]
    public async Task RunNowAsync_triggers_install_immediately_without_touching_NextRunAt()
    {
        await AddAgentAsync("host-1");
        var farFuture = DateTimeOffset.UtcNow.AddDays(30);
        var created = await _service.CreateAsync(OnceRequest(["host-1"], farFuture), "admin");

        var triggered = await _service.RunNowAsync(created.Schedule!.Id, "admin");

        Assert.True(triggered);
        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.NotNull(agent.PendingInstallRequestedAt);

        var schedule = await _service.GetByIdAsync(created.Schedule.Id);
        Assert.Equal(farFuture, schedule!.NextRunAt);

        var runs = await _service.GetRunsAsync(created.Schedule.Id);
        Assert.Single(runs);
        Assert.Equal(ScheduleRunActionStatus.Pending, runs[0].Agents.Single().InstallStatus);
    }

    [Fact]
    public async Task FireDueSchedulesAsync_fires_a_due_recurring_schedule_and_advances_NextRunAt_into_the_future()
    {
        await AddAgentAsync("host-1");
        var request = OnceRequest(["host-1"], DateTimeOffset.UtcNow) with
        {
            ScheduleType = ScheduleType.Recurring,
            Pattern = SchedulePattern.IntervalDays,
            IntervalDays = 1,
            IntervalStartDate = DateOnly.FromDateTime(DateTime.Today),
            OnceAt = null,
        };
        var created = await _service.CreateAsync(request, "admin");

        // Force it due right now regardless of exactly where the
        // calculator placed the next midnight boundary — the recurrence
        // math itself is covered by ScheduleRecurrenceCalculatorTests;
        // this test is only about the firing mechanism.
        var schedule = await _db.Schedules.SingleAsync(s => s.Id == created.Schedule!.Id);
        schedule.NextRunAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await _db.SaveChangesAsync();

        await _service.FireDueSchedulesAsync();

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.NotNull(agent.PendingInstallRequestedAt);

        var updated = await _service.GetByIdAsync(created.Schedule!.Id);
        Assert.NotNull(updated!.NextRunAt);
        Assert.True(updated.NextRunAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task FireDueSchedulesAsync_ignores_a_disabled_schedule_even_if_due()
    {
        await AddAgentAsync("host-1");
        var created = await _service.CreateAsync(OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1)), "admin");
        await _service.UpdateAsync(created.Schedule!.Id, OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1)) with { Enabled = false }, "admin");

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await _service.FireDueSchedulesAsync();

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.Null(agent.PendingInstallRequestedAt);
    }

    [Fact]
    public async Task ExpireMissedAsync_marks_a_stale_pending_install_as_missed_and_clears_the_agent()
    {
        await AddAgentAsync("host-1");
        var created = await _service.CreateAsync(OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1)) with { DeadlineHours = 1 }, "admin");
        await _service.RunNowAsync(created.Schedule!.Id, "admin");

        // Simulate the deadline having already passed.
        var run = await _db.ScheduleRuns.SingleAsync(r => r.ScheduleId == created.Schedule.Id);
        run.DeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        await _service.ExpireMissedAsync();

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.Null(agent.PendingInstallRequestedAt);
        Assert.Null(agent.PendingInstallScheduleRunId);

        var runs = await _service.GetRunsAsync(created.Schedule.Id);
        Assert.Equal(ScheduleRunActionStatus.Missed, runs[0].Agents.Single().InstallStatus);
    }

    [Fact]
    public async Task ExpireMissedAsync_does_not_touch_a_manually_triggered_install()
    {
        var agent = await AddAgentAsync("host-1");
        await _updateService.TriggerInstallAsync("host-1", "admin", updateItemIds: null);

        await _service.ExpireMissedAsync();

        var reloaded = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.NotNull(reloaded.PendingInstallRequestedAt);
    }

    [Fact]
    public async Task ExpireMissedAsync_sends_a_failure_notification_email_for_a_missed_action_when_NotifyOnFailure_is_true()
    {
        await AddAgentAsync("host-1");
        var created = await _service.CreateAsync(OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1)) with { DeadlineHours = 1, NotifyOnFailure = true }, "admin");
        await _service.RunNowAsync(created.Schedule!.Id, "admin");

        var run = await _db.ScheduleRuns.SingleAsync(r => r.ScheduleId == created.Schedule.Id);
        run.DeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        await _service.ExpireMissedAsync();

        var sent = Assert.Single(_email.SentNotifications);
        Assert.Equal("admin@example.com", sent.To);
        Assert.NotEmpty(await _db.AuditLogEntries.Where(e => e.Action == "schedule.run.missed.notified").ToListAsync());
    }

    [Fact]
    public async Task ExpireMissedAsync_does_not_send_a_failure_notification_email_when_NotifyOnFailure_is_false()
    {
        await AddAgentAsync("host-1");
        var created = await _service.CreateAsync(OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1)) with { DeadlineHours = 1, NotifyOnFailure = false }, "admin");
        await _service.RunNowAsync(created.Schedule!.Id, "admin");

        var run = await _db.ScheduleRuns.SingleAsync(r => r.ScheduleId == created.Schedule.Id);
        run.DeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        await _service.ExpireMissedAsync();

        Assert.Empty(_email.SentNotifications);
    }

    [Fact]
    public async Task ExpireMissedAsync_does_not_send_a_failure_notification_email_for_a_merely_skipped_conditional_reboot()
    {
        // A Skipped conditional reboot (never confirmed necessary) is an
        // expected, benign outcome — only a genuine Missed transition is
        // worth emailing about. The install itself is acknowledged as
        // Succeeded before the deadline expires, so only the reboot watch
        // (still AwaitingInstallResult) is left for ExpireMissedAsync to
        // resolve, isolating the Skipped-only case from an install miss.
        await AddAgentAsync("host-1", rebootRequired: false);
        var request = OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1), actionInstall: true, actionReboot: true, rebootOnlyIfRequired: true) with { DeadlineHours = 1 };
        var created = await _service.CreateAsync(request, "admin");
        await _service.RunNowAsync(created.Schedule!.Id, "admin");
        await _updateService.AcknowledgeInstallAsync("host-1", InstallOutcome.Succeeded, null);

        var run = await _db.ScheduleRuns.SingleAsync(r => r.ScheduleId == created.Schedule!.Id);
        run.DeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        await _service.ExpireMissedAsync();

        var runs = await _service.GetRunsAsync(created.Schedule.Id);
        Assert.Equal(ScheduleRunActionStatus.Skipped, runs[0].Agents.Single().RebootStatus);
        Assert.Empty(_email.SentNotifications);
    }

    [Fact]
    public async Task A_reboot_only_schedule_with_only_if_required_skips_an_agent_that_does_not_need_a_reboot()
    {
        await AddAgentAsync("host-1", rebootRequired: false);
        var request = OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1), actionInstall: false, actionReboot: true, rebootOnlyIfRequired: true);
        var created = await _service.CreateAsync(request, "admin");

        await _service.RunNowAsync(created.Schedule!.Id, "admin");

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.Null(agent.PendingRebootRequestedAt);

        var runs = await _service.GetRunsAsync(created.Schedule.Id);
        Assert.Equal(ScheduleRunActionStatus.Skipped, runs[0].Agents.Single().RebootStatus);
    }

    [Fact]
    public async Task A_reboot_only_schedule_with_only_if_required_triggers_an_agent_that_does_need_a_reboot()
    {
        await AddAgentAsync("host-1", rebootRequired: true);
        var request = OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1), actionInstall: false, actionReboot: true, rebootOnlyIfRequired: true);
        var created = await _service.CreateAsync(request, "admin");

        await _service.RunNowAsync(created.Schedule!.Id, "admin");

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.NotNull(agent.PendingRebootRequestedAt);
    }

    [Fact]
    public async Task An_install_plus_conditional_reboot_schedule_waits_for_the_agent_to_report_reboot_required_before_triggering_it()
    {
        await AddAgentAsync("host-1", rebootRequired: false);
        var request = OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1), actionInstall: true, actionReboot: true, rebootOnlyIfRequired: true);
        var created = await _service.CreateAsync(request, "admin");

        await _service.RunNowAsync(created.Schedule!.Id, "admin");

        // Install was requested immediately; the reboot is only being watched for, not yet requested.
        var agentAfterFire = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.NotNull(agentAfterFire.PendingInstallRequestedAt);
        Assert.Null(agentAfterFire.PendingRebootRequestedAt);
        Assert.NotNull(agentAfterFire.PendingConditionalRebootScheduleRunId);

        var runsBeforeInstallCompletes = await _service.GetRunsAsync(created.Schedule!.Id);
        Assert.Equal(ScheduleRunActionStatus.AwaitingInstallResult, runsBeforeInstallCompletes[0].Agents.Single().RebootStatus);

        // The agent's next heartbeat reports the install actually required a reboot.
        await _registrationService.RecordAliveAsync("host-1", new AgentAliveRequest(null, null, null, null, RebootRequired: true));

        var agentAfterHeartbeat = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.NotNull(agentAfterHeartbeat.PendingRebootRequestedAt);
        Assert.Null(agentAfterHeartbeat.PendingConditionalRebootScheduleRunId);

        var runsAfterHeartbeat = await _service.GetRunsAsync(created.Schedule.Id);
        Assert.Equal(ScheduleRunActionStatus.Pending, runsAfterHeartbeat[0].Agents.Single().RebootStatus);
    }

    [Fact]
    public async Task An_install_plus_conditional_reboot_schedule_skips_the_reboot_once_its_deadline_passes_with_no_reboot_required()
    {
        await AddAgentAsync("host-1", rebootRequired: false);
        var request = OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1), actionInstall: true, actionReboot: true, rebootOnlyIfRequired: true) with { DeadlineHours = 1 };
        var created = await _service.CreateAsync(request, "admin");
        await _service.RunNowAsync(created.Schedule!.Id, "admin");

        var run = await _db.ScheduleRuns.SingleAsync(r => r.ScheduleId == created.Schedule!.Id);
        run.DeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        await _service.ExpireMissedAsync();

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.Null(agent.PendingConditionalRebootScheduleRunId);
        Assert.Null(agent.PendingRebootRequestedAt);

        var runs = await _service.GetRunsAsync(created.Schedule!.Id);
        Assert.Equal(ScheduleRunActionStatus.Skipped, runs[0].Agents.Single().RebootStatus);
    }

    [Fact]
    public async Task AcknowledgeInstallAsync_records_the_outcome_on_the_schedule_run()
    {
        await AddAgentAsync("host-1");
        var created = await _service.CreateAsync(OnceRequest(["host-1"], DateTimeOffset.UtcNow.AddSeconds(1)), "admin");
        await _service.RunNowAsync(created.Schedule!.Id, "admin");

        await _updateService.AcknowledgeInstallAsync("host-1", InstallOutcome.Succeeded, null);

        var runs = await _service.GetRunsAsync(created.Schedule.Id);
        Assert.Equal(ScheduleRunActionStatus.Delivered, runs[0].Agents.Single().InstallStatus);

        var agent = await _db.Agents.SingleAsync(a => a.Hostname == "host-1");
        Assert.Null(agent.PendingInstallScheduleRunId);
    }
}
