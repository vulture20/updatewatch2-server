using Cronos;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Schedules;

/// <summary>
/// Pure, DB-free computation of a <see cref="Schedule"/>'s next firing
/// time — the same testable-orchestrator split this codebase already uses
/// elsewhere (e.g. <see cref="UpdateFilters.UpdateFilterMatcher"/>,
/// <see cref="Certificates.CertificateRejectionClassifier"/>).
///
/// Uses a single, admin-configured time zone (<see cref="Admin.IAdminSettingsStore.TimeZoneId"/>,
/// resolved by <see cref="ScheduleService"/> and passed in here — this
/// class itself stays DB/settings-unaware) rather than a per-schedule time
/// zone — this project has no per-agent/per-schedule time-zone concept
/// anywhere else today, and adding one would be a real scope increase for
/// a fleet that, in practice, is almost always deployed in a single time
/// zone. See updatewatch2-server#25's own "Nicht-Ziele" section.
///
/// This deliberately replaced an earlier version that used
/// <see cref="TimeZoneInfo.Local"/> (the server process's own OS time
/// zone) — found by a user report to be genuinely confusing in practice: a
/// container's OS time zone is UTC by default unless explicitly configured
/// with a <c>TZ</c> environment variable (an easy thing to forget, and
/// exactly what had happened), so a schedule's displayed next-run time
/// silently drifted from what the admin actually typed by a DST-dependent
/// amount (2h in summer/CEST, 1h in winter/CET, for a Europe/Berlin admin
/// against a UTC container) — the inconsistency being DST-dependent, not a
/// fixed offset, is what made it look like "sometimes CEST, sometimes
/// local time" rather than a simple, constant, easy-to-spot bug. A
/// <see cref="ScheduleType.Once"/> schedule was never affected by this,
/// since its <see cref="Schedule.OnceAt"/> is already a specific,
/// browser-converted UTC instant with no reinterpretation needed — only
/// <see cref="ScheduleType.Recurring"/>/<see cref="ScheduleType.Cron"/>,
/// whose <see cref="Schedule.TimeOfDay"/>/cron expression carry no time
/// zone of their own and previously relied on the (often wrong) OS zone to
/// supply one.
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
    /// <paramref name="timeZone"/> only matters for
    /// <see cref="ScheduleType.Recurring"/>/<see cref="ScheduleType.Cron"/>
    /// — a <see cref="ScheduleType.Once"/> schedule's <see cref="Schedule.OnceAt"/>
    /// is already an unambiguous instant.
    /// </summary>
    public static DateTimeOffset? ComputeNextRunAt(Schedule schedule, DateTimeOffset after, TimeZoneInfo timeZone)
    {
        if (schedule.ScheduleType == ScheduleType.Once)
        {
            return schedule.OnceAt is { } onceAt && onceAt >= after ? onceAt : null;
        }

        if (schedule.ScheduleType == ScheduleType.Cron)
        {
            return ComputeNextCron(schedule, after, timeZone);
        }

        return schedule.Pattern switch
        {
            SchedulePattern.Weekly => ComputeNextWeekly(schedule, after, timeZone),
            SchedulePattern.IntervalDays => ComputeNextInterval(schedule, after, timeZone),
            _ => null,
        };
    }

    /// <summary>
    /// Parses a standard 5-field cron expression, returning false (with
    /// <paramref name="cron"/> unset) on anything Cronos can't parse — the
    /// same signature shape used both by <see cref="ComputeNextCron"/> and
    /// by <see cref="ScheduleService"/>'s own request-time validation, so
    /// the two can never disagree about what counts as a valid expression.
    /// </summary>
    public static bool TryParseCron(string? expression, out CronExpression cron) =>
        TryParseCron(expression, out cron, out _);

    /// <summary>Same as the two-out-parameter overload, plus Cronos' own parse-error message for a request-validation <c>errorDetail</c> field — see <c>ApiErrorCode.ScheduleCronExpressionInvalid</c>.</summary>
    public static bool TryParseCron(string? expression, out CronExpression cron, out string? errorMessage)
    {
        cron = null!;
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            errorMessage = "Cron expression must not be empty.";
            return false;
        }

        try
        {
            cron = CronExpression.Parse(expression.Trim());
            return true;
        }
        catch (CronFormatException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static DateTimeOffset? ComputeNextCron(Schedule schedule, DateTimeOffset after, TimeZoneInfo timeZone)
    {
        if (!TryParseCron(schedule.CronExpression, out var cron))
        {
            return null;
        }

        var nextUtc = cron.GetNextOccurrence(after.UtcDateTime, timeZone, inclusive: true);
        return nextUtc is { } value ? new DateTimeOffset(value, TimeSpan.Zero) : null;
    }

    public static IReadOnlyList<DayOfWeek> ParseWeeklyDays(string? weeklyDays) =>
        string.IsNullOrEmpty(weeklyDays)
            ? []
            : weeklyDays.Split(DaySeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => Enum.Parse<DayOfWeek>(d.Trim()))
                .ToList();

    public static string FormatWeeklyDays(IReadOnlyList<DayOfWeek> days) =>
        string.Join(DaySeparator, days.Select(d => d.ToString()));

    private static DateTimeOffset? ComputeNextWeekly(Schedule schedule, DateTimeOffset after, TimeZoneInfo timeZone)
    {
        var days = ParseWeeklyDays(schedule.WeeklyDays);
        if (days.Count == 0)
        {
            return null;
        }

        // "Today" per the CONFIGURED time zone, not the server process's
        // own OS zone — using TimeZoneInfo.Local (or worse, machine UTC)
        // here would pick the wrong calendar day near a midnight boundary
        // whenever the two zones disagree, exactly the class of bug this
        // whole file's own doc comment now documents.
        var startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, timeZone).Date);
        for (var offset = 0; offset <= 7; offset++)
        {
            var candidateDate = startDate.AddDays(offset);
            if (!days.Contains(candidateDate.DayOfWeek))
            {
                continue;
            }

            var candidate = ToZonedDateTimeOffset(candidateDate, schedule.TimeOfDay, timeZone);
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

    private static DateTimeOffset? ComputeNextInterval(Schedule schedule, DateTimeOffset after, TimeZoneInfo timeZone)
    {
        if (schedule.IntervalDays is not { } intervalDays || intervalDays < 1 || schedule.IntervalStartDate is not { } startDate)
        {
            return null;
        }

        var candidateDate = startDate;
        var candidate = ToZonedDateTimeOffset(candidateDate, schedule.TimeOfDay, timeZone);
        while (candidate < after)
        {
            candidateDate = candidateDate.AddDays(intervalDays);
            candidate = ToZonedDateTimeOffset(candidateDate, schedule.TimeOfDay, timeZone);
        }

        return candidate;
    }

    /// <summary>
    /// Combines a calendar date and a bare time-of-day into the
    /// unambiguous instant that wall-clock moment represents in
    /// <paramref name="timeZone"/> — works for any IANA zone, not just the
    /// server's own OS zone, since <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/>
    /// treats an <see cref="DateTimeKind.Unspecified"/> value as already
    /// being expressed in that zone's own wall-clock time and returns the
    /// correct offset for that zone on that specific date (respecting its
    /// own DST rules), rather than the machine's.
    /// </summary>
    private static DateTimeOffset ToZonedDateTimeOffset(DateOnly date, TimeSpan timeOfDay, TimeZoneInfo timeZone)
    {
        var unspecified = date.ToDateTime(TimeOnly.FromTimeSpan(timeOfDay), DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
    }
}
