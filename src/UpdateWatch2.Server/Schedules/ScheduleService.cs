using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Agents;
using UpdateWatch2.Server.Api;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Updates;

namespace UpdateWatch2.Server.Schedules;

public class ScheduleService(AppDbContext db, IUpdateService updateService, IAgentService agentService, IAuditLogService auditLog) : IScheduleService
{
    public async Task<IReadOnlyList<ScheduleDto>> GetAllAsync(CancellationToken ct = default) =>
        (await db.Schedules.Include(s => s.Agents).OrderBy(s => s.Name).ToListAsync(ct))
            .Select(ToDto)
            .ToList();

    public async Task<ScheduleDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var schedule = await db.Schedules.Include(s => s.Agents).SingleOrDefaultAsync(s => s.Id == id, ct);
        return schedule is null ? null : ToDto(schedule);
    }

    public async Task<ScheduleResult> CreateAsync(UpsertScheduleRequest request, string createdBy, CancellationToken ct = default)
    {
        var validationError = await ValidateAsync(request, ct);
        if (validationError is not null)
        {
            return ScheduleResult.Failed(validationError.Value.Message, validationError.Value.Code, validationError.Value.Detail);
        }

        var schedule = new Schedule { Name = request.Name };
        ApplyRequest(schedule, request);

        db.Schedules.Add(schedule);
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(createdBy, "schedule.create", schedule.Name, ct);

        return ScheduleResult.Succeeded(ToDto(schedule));
    }

    public async Task<ScheduleResult> UpdateAsync(int id, UpsertScheduleRequest request, string updatedBy, CancellationToken ct = default)
    {
        var schedule = await db.Schedules.Include(s => s.Agents).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (schedule is null)
        {
            return ScheduleResult.Failed("Not found.");
        }

        var validationError = await ValidateAsync(request, ct);
        if (validationError is not null)
        {
            return ScheduleResult.Failed(validationError.Value.Message, validationError.Value.Code, validationError.Value.Detail);
        }

        ApplyRequest(schedule, request);
        schedule.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(updatedBy, "schedule.update", schedule.Name, ct);

        return ScheduleResult.Succeeded(ToDto(schedule));
    }

    public async Task<bool> DeleteAsync(int id, string deletedBy, CancellationToken ct = default)
    {
        var schedule = await db.Schedules.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (schedule is null)
        {
            return false;
        }

        // Cancel any still-outstanding pending action this schedule's
        // run(s) set — see IScheduleService.DeleteAsync's own doc comment.
        var runIds = await db.ScheduleRuns.Where(r => r.ScheduleId == id).Select(r => r.Id).ToListAsync(ct);
        if (runIds.Count > 0)
        {
            var affectedAgents = await db.Agents
                .Where(a =>
                    (a.PendingInstallScheduleRunId != null && runIds.Contains(a.PendingInstallScheduleRunId.Value))
                    || (a.PendingRebootScheduleRunId != null && runIds.Contains(a.PendingRebootScheduleRunId.Value))
                    || (a.PendingConditionalRebootScheduleRunId != null && runIds.Contains(a.PendingConditionalRebootScheduleRunId.Value)))
                .ToListAsync(ct);

            foreach (var agent in affectedAgents)
            {
                if (agent.PendingInstallScheduleRunId is not null && runIds.Contains(agent.PendingInstallScheduleRunId.Value))
                {
                    agent.PendingInstallRequestedAt = null;
                    agent.PendingInstallUpdateIds = null;
                    agent.PendingInstallScheduleRunId = null;
                }

                if (agent.PendingRebootScheduleRunId is not null && runIds.Contains(agent.PendingRebootScheduleRunId.Value))
                {
                    agent.PendingRebootRequestedAt = null;
                    agent.PendingRebootScheduleRunId = null;
                }

                if (agent.PendingConditionalRebootScheduleRunId is not null && runIds.Contains(agent.PendingConditionalRebootScheduleRunId.Value))
                {
                    agent.PendingConditionalRebootScheduleRunId = null;
                }
            }
        }

        // Cascades ScheduleAgent + ScheduleRun (+ ScheduleRunAgent) via the FK configuration in AppDbContext.
        db.Schedules.Remove(schedule);
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(deletedBy, "schedule.delete", schedule.Name, ct);

        return true;
    }

    public async Task<bool> RunNowAsync(int id, string triggeredBy, CancellationToken ct = default)
    {
        var schedule = await db.Schedules.Include(s => s.Agents).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (schedule is null)
        {
            return false;
        }

        // Deliberately does not touch schedule.NextRunAt — a manual "run
        // now" is independent of, and never disturbs, the regular
        // recurrence (updatewatch2-server#25).
        await FireAsync(schedule, DateTimeOffset.UtcNow, triggeredBy, ct);
        return true;
    }

    public async Task<IReadOnlyList<ScheduleRunDto>> GetRunsAsync(int scheduleId, CancellationToken ct = default) =>
        await db.ScheduleRuns
            .Include(r => r.Agents)
            .Where(r => r.ScheduleId == scheduleId)
            .OrderByDescending(r => r.Id)
            .Select(r => new ScheduleRunDto(
                r.Id, r.FiredAt, r.DeadlineAt, r.ActionInstallSnapshot, r.ActionRebootSnapshot, r.RebootOnlyIfRequiredSnapshot,
                r.Agents.Select(a => new ScheduleRunAgentDto(a.Hostname, a.InstallStatus, a.RebootStatus, a.ErrorDetail)).ToList()))
            .ToListAsync(ct);

    public async Task FireDueSchedulesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Same EF-Core-on-SQLite DateTimeOffset-comparison limitation as
        // ExpireMissedAsync above — project the enabled/candidate rows
        // down to plain fields, decide "is it due" client-side, then load
        // the full (trackable) entities for just the resulting ids.
        var candidates = await db.Schedules
            .Where(s => s.Enabled && s.NextRunAt != null)
            .Select(s => new { s.Id, s.NextRunAt })
            .ToListAsync(ct);
        var dueIds = candidates.Where(c => c.NextRunAt <= now).Select(c => c.Id).ToList();

        var due = dueIds.Count == 0
            ? []
            : await db.Schedules.Include(s => s.Agents).Where(s => dueIds.Contains(s.Id)).ToListAsync(ct);

        foreach (var schedule in due)
        {
            await FireAsync(schedule, now, "system", ct);
            // Strictly after "now" so the occurrence just fired is never
            // re-selected as its own "next" run.
            schedule.NextRunAt = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, now.AddSeconds(1));
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task ExpireMissedAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // EF Core on SQLite can't translate a DateTimeOffset comparison
        // operator in a LINQ predicate — a confirmed, repeated finding
        // elsewhere in this codebase (see AuditLogService.PurgeOlderThanAsync/
        // CertificateRejectionService.GetStatusAsync's own comments on the
        // identical gap). Same workaround: project down to the plain
        // fields needed, filter for staleness client-side, then load the
        // full (trackable) entities for just the resulting keys.
        var candidates = await db.ScheduleRunAgents
            .Where(ra => ra.InstallStatus == ScheduleRunActionStatus.Pending
                || ra.RebootStatus == ScheduleRunActionStatus.Pending
                || ra.RebootStatus == ScheduleRunActionStatus.AwaitingInstallResult)
            .Select(ra => new { ra.ScheduleRunId, ra.Hostname, ra.ScheduleRun!.DeadlineAt })
            .ToListAsync(ct);

        var expiredKeys = candidates.Where(c => c.DeadlineAt < now).Select(c => (c.ScheduleRunId, c.Hostname)).ToHashSet();
        if (expiredKeys.Count == 0)
        {
            return;
        }

        var runIds = expiredKeys.Select(k => k.ScheduleRunId).Distinct().ToList();
        var expired = (await db.ScheduleRunAgents
                .Include(ra => ra.ScheduleRun!)
                .ThenInclude(r => r.Schedule)
                .Where(ra => runIds.Contains(ra.ScheduleRunId))
                .ToListAsync(ct))
            .Where(ra => expiredKeys.Contains((ra.ScheduleRunId, ra.Hostname)))
            .ToList();

        var hostnames = expired.Select(ra => ra.Hostname).Distinct().ToList();
        var agentsByHostname = await db.Agents.Where(a => hostnames.Contains(a.Hostname)).ToDictionaryAsync(a => a.Hostname, ct);

        foreach (var runAgent in expired)
        {
            agentsByHostname.TryGetValue(runAgent.Hostname, out var agent);
            var runId = runAgent.ScheduleRunId;

            if (runAgent.InstallStatus == ScheduleRunActionStatus.Pending)
            {
                runAgent.InstallStatus = ScheduleRunActionStatus.Missed;
                if (agent is not null && agent.PendingInstallScheduleRunId == runId)
                {
                    agent.PendingInstallRequestedAt = null;
                    agent.PendingInstallUpdateIds = null;
                    agent.PendingInstallScheduleRunId = null;
                }
            }

            if (runAgent.RebootStatus == ScheduleRunActionStatus.Pending)
            {
                runAgent.RebootStatus = ScheduleRunActionStatus.Missed;
                if (agent is not null && agent.PendingRebootScheduleRunId == runId)
                {
                    agent.PendingRebootRequestedAt = null;
                    agent.PendingRebootScheduleRunId = null;
                }
            }
            else if (runAgent.RebootStatus == ScheduleRunActionStatus.AwaitingInstallResult)
            {
                // Deadline passed before this agent ever confirmed a
                // reboot was actually needed — nothing to deliver.
                runAgent.RebootStatus = ScheduleRunActionStatus.Skipped;
                if (agent is not null && agent.PendingConditionalRebootScheduleRunId == runId)
                {
                    agent.PendingConditionalRebootScheduleRunId = null;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        foreach (var group in expired.GroupBy(ra => ra.ScheduleRunId))
        {
            var scheduleName = group.First().ScheduleRun!.Schedule!.Name;
            await auditLog.LogAsync("system", "schedule.run.missed", $"{scheduleName}: {group.Count()} agent action(s) expired", ct);
        }
    }

    /// <summary>
    /// The actual firing logic, shared by <see cref="FireDueSchedulesAsync"/>
    /// and <see cref="RunNowAsync"/> — creates the <see cref="ScheduleRun"/>/
    /// <see cref="ScheduleRunAgent"/> rows and delegates the real trigger
    /// delivery to the existing <see cref="IUpdateService"/>/<see cref="IAgentService"/>
    /// bulk methods, exactly the same ones the admin UI's own bulk-action
    /// toolbar already uses — see updatewatch2-server#25's design.
    /// </summary>
    private async Task FireAsync(Schedule schedule, DateTimeOffset firedAt, string triggeredBy, CancellationToken ct)
    {
        schedule.LastRunAt = firedAt;

        var run = new ScheduleRun
        {
            ScheduleId = schedule.Id,
            FiredAt = firedAt,
            DeadlineAt = firedAt.AddHours(schedule.DeadlineHours),
            ActionInstallSnapshot = schedule.ActionInstall,
            ActionRebootSnapshot = schedule.ActionReboot,
            RebootOnlyIfRequiredSnapshot = schedule.RebootOnlyIfRequired,
        };
        db.ScheduleRuns.Add(run);
        await db.SaveChangesAsync(ct); // assigns run.Id, needed by every ScheduleRunAgent row below

        var hostnames = schedule.Agents.Select(a => a.Hostname).ToList();
        var agentsByHostname = await db.Agents.Where(a => hostnames.Contains(a.Hostname)).ToDictionaryAsync(a => a.Hostname, ct);

        var installHostnames = new List<string>();
        var rebootHostnames = new List<string>();
        var conditionalRebootHostnames = new List<string>();

        foreach (var hostname in hostnames)
        {
            var runAgent = new ScheduleRunAgent { ScheduleRunId = run.Id, Hostname = hostname };
            agentsByHostname.TryGetValue(hostname, out var agent);

            if (schedule.ActionInstall)
            {
                runAgent.InstallStatus = ScheduleRunActionStatus.Pending;
                installHostnames.Add(hostname);
            }

            if (schedule.ActionReboot)
            {
                if (schedule.ActionInstall && schedule.RebootOnlyIfRequired)
                {
                    // Can only be decided once the install actually
                    // completes — see AgentRegistrationService.RecordAliveAsync's
                    // conditional-reboot-after-install watch.
                    runAgent.RebootStatus = ScheduleRunActionStatus.AwaitingInstallResult;
                    conditionalRebootHostnames.Add(hostname);
                }
                else if (schedule.RebootOnlyIfRequired)
                {
                    var required = agent?.RebootRequired ?? false;
                    runAgent.RebootStatus = required ? ScheduleRunActionStatus.Pending : ScheduleRunActionStatus.Skipped;
                    if (required)
                    {
                        rebootHostnames.Add(hostname);
                    }
                }
                else
                {
                    runAgent.RebootStatus = ScheduleRunActionStatus.Pending;
                    rebootHostnames.Add(hostname);
                }
            }

            db.ScheduleRunAgents.Add(runAgent);
        }

        await db.SaveChangesAsync(ct);

        if (installHostnames.Count > 0)
        {
            await updateService.TriggerInstallManyAsync(installHostnames, triggeredBy, scheduleRunId: run.Id, ct: ct);
        }

        if (rebootHostnames.Count > 0)
        {
            await agentService.TriggerRebootManyAsync(rebootHostnames, triggeredBy, scheduleRunId: run.Id, ct: ct);
        }

        if (conditionalRebootHostnames.Count > 0)
        {
            var watchedAgents = await db.Agents.Where(a => conditionalRebootHostnames.Contains(a.Hostname)).ToListAsync(ct);
            foreach (var agent in watchedAgents)
            {
                agent.PendingConditionalRebootScheduleRunId = run.Id;
            }

            await db.SaveChangesAsync(ct);
        }

        await auditLog.LogAsync(
            "system",
            "schedule.run.fired",
            $"{schedule.Name} ({hostnames.Count} agent(s): {installHostnames.Count} install, {rebootHostnames.Count} reboot, {conditionalRebootHostnames.Count} awaiting install result)",
            ct);
    }

    private static void ApplyRequest(Schedule schedule, UpsertScheduleRequest request)
    {
        schedule.Name = request.Name;
        schedule.Enabled = request.Enabled;
        schedule.ScheduleType = request.ScheduleType;
        schedule.Pattern = request.Pattern;
        schedule.OnceAt = request.OnceAt;
        schedule.WeeklyDays = request.WeeklyDays is null
            ? null
            : ScheduleRecurrenceCalculator.FormatWeeklyDays(request.WeeklyDays.Select(Enum.Parse<DayOfWeek>).ToList());
        schedule.TimeOfDay = request.TimeOfDay;
        schedule.IntervalDays = request.IntervalDays;
        schedule.IntervalStartDate = request.IntervalStartDate;
        schedule.ActionInstall = request.ActionInstall;
        schedule.ActionReboot = request.ActionReboot;
        schedule.RebootOnlyIfRequired = request.RebootOnlyIfRequired;
        schedule.DeadlineHours = request.DeadlineHours;

        var newHostnames = request.Hostnames.ToHashSet();
        var existingHostnames = schedule.Agents.Select(a => a.Hostname).ToHashSet();
        schedule.Agents.RemoveAll(a => !newHostnames.Contains(a.Hostname));
        foreach (var hostname in newHostnames.Except(existingHostnames))
        {
            schedule.Agents.Add(new ScheduleAgent { ScheduleId = schedule.Id, Hostname = hostname });
        }

        // Recomputed from "now" on every create/update — for a Recurring
        // schedule this makes an edited time/pattern apply to the very
        // next occurrence rather than only the one after whatever was
        // already scheduled under the old values; for a Once schedule,
        // editing its date effectively restarts it, the expected result
        // of "I changed the date". Null while disabled — see
        // Schedule.NextRunAt's own doc comment.
        schedule.NextRunAt = schedule.Enabled ? ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, DateTimeOffset.UtcNow) : null;
    }

    private static ScheduleStatus ComputeStatus(Schedule schedule) => !schedule.Enabled
        ? ScheduleStatus.Paused
        : schedule.ScheduleType == ScheduleType.Once && schedule.NextRunAt is null
            ? ScheduleStatus.Completed
            : ScheduleStatus.Active;

    private static ScheduleDto ToDto(Schedule schedule) => new(
        schedule.Id,
        schedule.Name,
        schedule.Enabled,
        ComputeStatus(schedule),
        schedule.ScheduleType,
        schedule.Pattern,
        schedule.OnceAt,
        schedule.Pattern == SchedulePattern.Weekly
            ? ScheduleRecurrenceCalculator.ParseWeeklyDays(schedule.WeeklyDays).Select(d => d.ToString()).ToList()
            : null,
        schedule.TimeOfDay,
        schedule.IntervalDays,
        schedule.IntervalStartDate,
        schedule.ActionInstall,
        schedule.ActionReboot,
        schedule.RebootOnlyIfRequired,
        schedule.DeadlineHours,
        schedule.NextRunAt,
        schedule.LastRunAt,
        schedule.Agents.Select(a => a.Hostname).OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToList());

    private async Task<(ApiErrorCode Code, string Message, string? Detail)?> ValidateAsync(UpsertScheduleRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return (ApiErrorCode.ScheduleNameRequired, "Name must not be empty.", null);
        }

        if (request.Hostnames.Count == 0)
        {
            return (ApiErrorCode.ScheduleAgentsRequired, "At least one agent must be selected.", null);
        }

        if (!request.ActionInstall && !request.ActionReboot)
        {
            return (ApiErrorCode.ScheduleActionRequired, "At least one action (install or reboot) must be selected.", null);
        }

        if (request.DeadlineHours < 1)
        {
            return (ApiErrorCode.ScheduleDeadlineHoursInvalid, "The deadline must be at least 1 hour.", null);
        }

        if (request.ScheduleType == ScheduleType.Once)
        {
            if (request.OnceAt is not { } onceAt)
            {
                return (ApiErrorCode.ScheduleOnceAtRequired, "A date/time is required for a one-time schedule.", null);
            }

            if (onceAt <= DateTimeOffset.UtcNow)
            {
                return (ApiErrorCode.ScheduleOnceAtInPast, "The date/time must be in the future.", null);
            }
        }
        else
        {
            switch (request.Pattern)
            {
                case SchedulePattern.Weekly:
                    if (request.WeeklyDays is null || request.WeeklyDays.Count == 0)
                    {
                        return (ApiErrorCode.ScheduleWeeklyDaysRequired, "At least one weekday is required.", null);
                    }

                    break;
                case SchedulePattern.IntervalDays:
                    if (request.IntervalDays is not { } intervalDays || intervalDays < 1)
                    {
                        return (ApiErrorCode.ScheduleIntervalDaysInvalid, "The interval must be at least 1 day.", null);
                    }

                    if (request.IntervalStartDate is null)
                    {
                        return (ApiErrorCode.ScheduleIntervalStartDateRequired, "A start date is required.", null);
                    }

                    break;
                default:
                    return (ApiErrorCode.SchedulePatternRequired, "A recurrence pattern is required.", null);
            }
        }

        var existingHostnames = await db.Agents.Where(a => request.Hostnames.Contains(a.Hostname)).Select(a => a.Hostname).ToListAsync(ct);
        var unknown = request.Hostnames.Except(existingHostnames).ToList();
        if (unknown.Count > 0)
        {
            var detail = string.Join(", ", unknown);
            return (ApiErrorCode.ScheduleUnknownAgents, $"Unknown agent(s): {detail}", detail);
        }

        return null;
    }
}
