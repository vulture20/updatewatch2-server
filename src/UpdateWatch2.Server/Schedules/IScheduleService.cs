namespace UpdateWatch2.Server.Schedules;

public interface IScheduleService
{
    Task<IReadOnlyList<ScheduleDto>> GetAllAsync(CancellationToken ct = default);

    Task<ScheduleDto?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<ScheduleResult> CreateAsync(UpsertScheduleRequest request, string createdBy, CancellationToken ct = default);

    Task<ScheduleResult> UpdateAsync(int id, UpsertScheduleRequest request, string updatedBy, CancellationToken ct = default);

    /// <summary>
    /// Deletes the schedule and, per updatewatch2-server#25's design
    /// decision, cancels any still-outstanding pending install/reboot
    /// trigger its most recent run set — an agent that hasn't checked in
    /// yet never receives an action from a schedule that no longer exists.
    /// Returns false if no schedule with that id exists.
    /// </summary>
    Task<bool> DeleteAsync(int id, string deletedBy, CancellationToken ct = default);

    /// <summary>
    /// Fires the schedule immediately, independent of (and without
    /// affecting) its regular <see cref="Db.Entities.Schedule.NextRunAt"/> —
    /// the admin UI's "run now" button. Returns false if no schedule with
    /// that id exists.
    /// </summary>
    Task<bool> RunNowAsync(int id, string triggeredBy, CancellationToken ct = default);

    /// <summary>Run history for one schedule, newest first.</summary>
    Task<IReadOnlyList<ScheduleRunDto>> GetRunsAsync(int scheduleId, CancellationToken ct = default);

    /// <summary>Fires every enabled schedule whose <see cref="Db.Entities.Schedule.NextRunAt"/> is due — driven by <see cref="ScheduleWorker"/>, not called from any admin-facing endpoint.</summary>
    Task FireDueSchedulesAsync(CancellationToken ct = default);

    /// <summary>
    /// Sweeps every still-outstanding schedule-originated pending action
    /// whose run has passed its deadline, marking it Missed (or, for a
    /// conditional-reboot watch, Skipped — no reboot was ever confirmed
    /// necessary) and clearing the corresponding <c>Agent.Pending*</c>
    /// field so a very-late agent doesn't act on a stale trigger. Driven by
    /// <see cref="ScheduleWorker"/>.
    /// </summary>
    Task ExpireMissedAsync(CancellationToken ct = default);
}
