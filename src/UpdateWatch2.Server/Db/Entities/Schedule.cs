using System.Text.Json.Serialization;

namespace UpdateWatch2.Server.Db.Entities;

// JsonStringEnumConverter-decorated on both — this codebase's established,
// live-confirmed convention for every enum-like wire value (see e.g.
// Updates.InstallOutcome's own doc comment for the real bug this avoids:
// System.Text.Json's default numeric encoding would otherwise 400 a
// hand-typed request body and render as bare numbers in the admin UI's
// API responses). EF Core stores these as its own plain INTEGER columns
// regardless — this attribute only affects System.Text.Json (de)serialization
// when ScheduleDto returns them directly.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScheduleType
{
    Once,
    Recurring,

    /// <summary>Standard 5-field cron expression (<see cref="Schedule.CronExpression"/>), parsed via Cronos and evaluated in the server's own local time zone — same no-per-schedule-time-zone reasoning as <see cref="Recurring"/>.</summary>
    Cron,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SchedulePattern
{
    /// <summary>Specific weekdays (<see cref="Schedule.WeeklyDays"/>) at <see cref="Schedule.TimeOfDay"/>.</summary>
    Weekly,

    /// <summary>Every <see cref="Schedule.IntervalDays"/> days, anchored at <see cref="Schedule.IntervalStartDate"/>, at <see cref="Schedule.TimeOfDay"/>.</summary>
    IntervalDays,
}

/// <summary>
/// A named, admin-defined maintenance window: a fixed list of agents
/// (<see cref="ScheduleAgent"/>), a firing pattern, and an action
/// (install updates and/or reboot). Deliberately a pure server-side
/// orchestration layer — firing a schedule does nothing the admin
/// couldn't already do by hand via <see cref="Updates.IUpdateService.TriggerInstallManyAsync"/>/
/// <see cref="Agents.IAgentService.TriggerRebootManyAsync"/>, just on a
/// timer instead of a button click. No agent-visible wire change, no
/// protocol bump — see updatewatch2-server#25.
/// </summary>
public class Schedule
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Pause without deleting — a disabled schedule is never picked up by <see cref="Schedules.ScheduleWorker"/>, even if <see cref="NextRunAt"/> is technically due.</summary>
    public bool Enabled { get; set; } = true;

    public ScheduleType ScheduleType { get; set; }

    /// <summary>Only set when <see cref="ScheduleType"/> is <see cref="Db.Entities.ScheduleType.Recurring"/>.</summary>
    public SchedulePattern? Pattern { get; set; }

    /// <summary>Only set when <see cref="ScheduleType"/> is <see cref="Db.Entities.ScheduleType.Once"/>.</summary>
    public DateTimeOffset? OnceAt { get; set; }

    /// <summary>
    /// Comma-separated <see cref="DayOfWeek"/> names (e.g. "Monday,Wednesday,Friday")
    /// — only set when <see cref="Pattern"/> is <see cref="SchedulePattern.Weekly"/>.
    /// A plain delimited string rather than a normalized child table: the
    /// set is small, fixed-vocabulary, and never queried by day — the same
    /// reasoning <see cref="Agent.PendingInstallUpdateIds"/> already applies
    /// to a small ad-hoc list that's only ever read back as a whole.
    /// </summary>
    public string? WeeklyDays { get; set; }

    /// <summary>Time of day this schedule fires, in the server's own local time zone (see <see cref="Schedules.ScheduleRecurrenceCalculator"/>'s doc comment on why no per-schedule time zone is offered).</summary>
    public TimeSpan TimeOfDay { get; set; }

    /// <summary>Only set when <see cref="Pattern"/> is <see cref="SchedulePattern.IntervalDays"/>.</summary>
    public int? IntervalDays { get; set; }

    /// <summary>Anchor date for the interval calculation — only set when <see cref="Pattern"/> is <see cref="SchedulePattern.IntervalDays"/>.</summary>
    public DateOnly? IntervalStartDate { get; set; }

    /// <summary>Standard 5-field cron expression — only set when <see cref="ScheduleType"/> is <see cref="Db.Entities.ScheduleType.Cron"/>.</summary>
    public string? CronExpression { get; set; }

    public bool ActionInstall { get; set; }

    public bool ActionReboot { get; set; }

    /// <summary>
    /// Only meaningful when <see cref="ActionReboot"/> is true. For a
    /// reboot-only schedule, checked once at fire time. For a combined
    /// install+reboot schedule, the reboot need can only be known after
    /// the install actually completes — see
    /// <see cref="Agents.AgentRegistrationService.RecordAliveAsync"/>'s
    /// conditional-reboot-after-install watch, driven by
    /// <see cref="ScheduleRunAgent.RebootStatus"/>'s <c>AwaitingInstallResult</c>
    /// state.
    /// </summary>
    public bool RebootOnlyIfRequired { get; set; }

    /// <summary>
    /// How long an agent that hasn't picked up this schedule's action yet
    /// (offline, or — for the conditional-reboot case — hasn't yet
    /// reported back after the install) is still waited for before the
    /// action is abandoned and logged as missed rather than staying
    /// pending indefinitely, unlike a manually-triggered install/reboot.
    /// See <see cref="ScheduleRun.DeadlineAt"/>.
    /// </summary>
    public int DeadlineHours { get; set; } = 4;

    /// <summary>
    /// Whether a failed install/reboot acknowledgement or a missed
    /// (deadline-expired) action for this schedule should send a
    /// notification email, reusing the same
    /// <see cref="Notifications.IEmailNotificationService.SendNotificationAsync"/>
    /// bilingual primitive and <c>IsConfigured</c>-plus-recipient guard every
    /// other automated notification in this codebase already uses. Default
    /// true — this is a per-schedule opt-out, not opt-in. Audit logging of
    /// the same failure/miss is unconditional regardless of this flag; it
    /// only gates the email attempt.
    /// </summary>
    public bool NotifyOnFailure { get; set; } = true;

    /// <summary>
    /// Pre-computed next firing time, read by <see cref="Schedules.ScheduleWorker"/>
    /// every tick — null once a <see cref="Db.Entities.ScheduleType.Once"/>
    /// schedule has fired (which <see cref="Schedules.ScheduleService"/>'s
    /// DTO projection reads as "Completed", without a separate persisted
    /// completion flag — see <see cref="ScheduleRun"/> for the per-agent
    /// detail behind that summary), or while <see cref="Enabled"/> is false
    /// (a disabled schedule's <see cref="NextRunAt"/> is left untouched but
    /// ignored, then recomputed fresh from "now" the moment it's
    /// re-enabled — no catch-up firing for the paused period).
    /// </summary>
    public DateTimeOffset? NextRunAt { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ScheduleAgent> Agents { get; set; } = [];
}
