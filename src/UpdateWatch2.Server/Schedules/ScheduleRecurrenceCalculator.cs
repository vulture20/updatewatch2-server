using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Schedules;

/// <summary>
/// Pure, DB-free computation of a <see cref="Schedule"/>'s next firing
/// time — the same testable-orchestrator split this codebase already uses
/// elsewhere (e.g. <see cref="UpdateFilters.UpdateFilterMatcher"/>,
/// <see cref="Certificates.CertificateRejectionClassifier"/>).
///
/// Deliberately uses the server process's own local time zone
/// (<see cref="TimeZoneInfo.Local"/>) rather than a per-schedule time
/// zone — this project has no time-zone concept anywhere else today (every
/// existing periodic worker just uses server wall-clock time), and adding
/// one would be a real scope increase for a fleet that, in practice, is
/// almost always deployed in a single time zone. See
/// updatewatch2-server#25's own "Nicht-Ziele" section.
/// </summary>
public static class ScheduleRecurrenceCalculator
{
    private const char DaySeparator = ',';

    /// <summary>
    /// The earliest valid firing time that is at or after
    /// <paramref name="after"/> — pass "now" to compute a freshly
    /// created/re-enabled schedule's next run, or "the instant this
    /// schedule just fired, plus a moment" to compute the FOLLOWING
    /// occurrence without re-selecting the one that just fired. Returns
    /// null once a <see cref="ScheduleType.Once"/> schedule has passed (no
    /// more runs) or a <see cref="ScheduleType.Recurring"/> schedule is
    /// missing the fields its own <see cref="Schedule.Pattern"/> needs.
    /// </summary>
    public static DateTimeOffset? ComputeNextRunAt(Schedule schedule, DateTimeOffset after)
    {
        if (schedule.ScheduleType == ScheduleType.Once)
        {
            return schedule.OnceAt is { } onceAt && onceAt >= after ? onceAt : null;
        }

        return schedule.Pattern switch
        {
            SchedulePattern.Weekly => ComputeNextWeekly(schedule, after),
            SchedulePattern.IntervalDays => ComputeNextInterval(schedule, after),
            _ => null,
        };
    }

    public static IReadOnlyList<DayOfWeek> ParseWeeklyDays(string? weeklyDays) =>
        string.IsNullOrEmpty(weeklyDays)
            ? []
            : weeklyDays.Split(DaySeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => Enum.Parse<DayOfWeek>(d.Trim()))
                .ToList();

    public static string FormatWeeklyDays(IReadOnlyList<DayOfWeek> days) =>
        string.Join(DaySeparator, days.Select(d => d.ToString()));

    private static DateTimeOffset? ComputeNextWeekly(Schedule schedule, DateTimeOffset after)
    {
        var days = ParseWeeklyDays(schedule.WeeklyDays);
        if (days.Count == 0)
        {
            return null;
        }

        var startDate = DateOnly.FromDateTime(after.ToLocalTime().Date);
        for (var offset = 0; offset <= 7; offset++)
        {
            var candidateDate = startDate.AddDays(offset);
            if (!days.Contains(candidateDate.DayOfWeek))
            {
                continue;
            }

            var candidate = ToLocalDateTimeOffset(candidateDate, schedule.TimeOfDay);
            if (candidate >= after)
            {
                return candidate;
            }
        }

        // Unreachable when days.Count > 0: the 7-day window above always
        // contains every weekday at least once, and the loop only skips a
        // same-day match whose time has already passed today, which the
        // wrap-around to next week's occurrence of that same day covers.
        return null;
    }

    private static DateTimeOffset? ComputeNextInterval(Schedule schedule, DateTimeOffset after)
    {
        if (schedule.IntervalDays is not { } intervalDays || intervalDays < 1 || schedule.IntervalStartDate is not { } startDate)
        {
            return null;
        }

        var candidateDate = startDate;
        var candidate = ToLocalDateTimeOffset(candidateDate, schedule.TimeOfDay);
        while (candidate < after)
        {
            candidateDate = candidateDate.AddDays(intervalDays);
            candidate = ToLocalDateTimeOffset(candidateDate, schedule.TimeOfDay);
        }

        return candidate;
    }

    private static DateTimeOffset ToLocalDateTimeOffset(DateOnly date, TimeSpan timeOfDay) =>
        new(date.ToDateTime(TimeOnly.FromTimeSpan(timeOfDay), DateTimeKind.Local));
}
