namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// One actual firing of a <see cref="Schedule"/> — exists so the admin UI
/// can show a history ("who got this run, who missed it") and so a
/// currently-pending install/reboot trigger can be traced back to the
/// schedule/run that set it (<see cref="Agent.PendingInstallScheduleRunId"/>/
/// <see cref="Agent.PendingRebootScheduleRunId"/>), which is what lets the
/// deadline-expiry sweep in <see cref="Schedules.ScheduleService"/> tell a
/// schedule-originated pending trigger (subject to <see cref="DeadlineAt"/>)
/// apart from a manually-triggered one (which stays pending indefinitely,
/// unchanged existing behavior).
/// </summary>
public class ScheduleRun
{
    public int Id { get; set; }

    public int ScheduleId { get; set; }

    public Schedule? Schedule { get; set; }

    public DateTimeOffset FiredAt { get; set; }

    /// <summary>
    /// <see cref="FiredAt"/> + the schedule's <see cref="Schedule.DeadlineHours"/>
    /// AT THE TIME this run fired — frozen here rather than read live off
    /// the schedule, so editing a schedule's deadline afterward only
    /// affects its future runs, never one already in flight.
    /// </summary>
    public DateTimeOffset DeadlineAt { get; set; }

    /// <summary>Snapshot of the schedule's action configuration at fire time — see <see cref="DeadlineAt"/>'s doc comment for why a snapshot, not a live read.</summary>
    public bool ActionInstallSnapshot { get; set; }

    public bool ActionRebootSnapshot { get; set; }

    public bool RebootOnlyIfRequiredSnapshot { get; set; }

    public List<ScheduleRunAgent> Agents { get; set; } = [];
}
