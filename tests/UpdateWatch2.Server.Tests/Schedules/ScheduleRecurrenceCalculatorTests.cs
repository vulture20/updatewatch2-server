using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Schedules;

namespace UpdateWatch2.Server.Tests.Schedules;

public class ScheduleRecurrenceCalculatorTests
{
    // A fixed, no-DST offset distinct from both UTC and whatever the CI
    // machine's own local time zone happens to be — using this (rather
    // than TimeZoneInfo.Local, as this file used to) is what actually
    // proves ComputeNextRunAt honors the PARAMETER, not the machine.
    private static readonly TimeZoneInfo FixedPlusFive = TimeZoneInfo.CreateCustomTimeZone("Fixed+05:00", TimeSpan.FromHours(5), "Fixed+05:00", "Fixed+05:00");

    [Fact]
    public void ComputeNextRunAt_for_a_future_Once_schedule_returns_its_own_date()
    {
        var onceAt = DateTimeOffset.UtcNow.AddDays(3);
        var schedule = new Schedule { Name = "once", ScheduleType = ScheduleType.Once, OnceAt = onceAt };

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, DateTimeOffset.UtcNow, TimeZoneInfo.Utc);

        Assert.Equal(onceAt, next);
    }

    [Fact]
    public void ComputeNextRunAt_for_an_Once_schedule_after_it_already_fired_returns_null()
    {
        var onceAt = DateTimeOffset.UtcNow.AddDays(-1);
        var schedule = new Schedule { Name = "once", ScheduleType = ScheduleType.Once, OnceAt = onceAt };

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, DateTimeOffset.UtcNow, TimeZoneInfo.Utc);

        Assert.Null(next);
    }

    [Fact]
    public void ComputeNextRunAt_for_Weekly_picks_the_earliest_matching_weekday_at_or_after_now()
    {
        // A Wednesday, well clear of any DST boundary, expressed directly
        // in FixedPlusFive's own offset.
        var wednesday = new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.FromHours(5));
        var schedule = new Schedule
        {
            Name = "weekly",
            ScheduleType = ScheduleType.Recurring,
            Pattern = SchedulePattern.Weekly,
            WeeklyDays = ScheduleRecurrenceCalculator.FormatWeeklyDays([DayOfWeek.Monday, DayOfWeek.Friday]),
            TimeOfDay = TimeSpan.FromHours(14),
        };

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, wednesday, FixedPlusFive);

        Assert.Equal(new DateTimeOffset(2026, 3, 6, 14, 0, 0, TimeSpan.FromHours(5)), next);
    }

    [Fact]
    public void ComputeNextRunAt_for_Weekly_wraps_to_next_week_once_todays_time_has_passed()
    {
        var mondayEvening = new DateTimeOffset(2026, 3, 2, 20, 0, 0, TimeSpan.FromHours(5));
        var schedule = new Schedule
        {
            Name = "weekly",
            ScheduleType = ScheduleType.Recurring,
            Pattern = SchedulePattern.Weekly,
            WeeklyDays = ScheduleRecurrenceCalculator.FormatWeeklyDays([DayOfWeek.Monday]),
            TimeOfDay = TimeSpan.FromHours(9),
        };

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, mondayEvening, FixedPlusFive);

        Assert.Equal(new DateTimeOffset(2026, 3, 9, 9, 0, 0, TimeSpan.FromHours(5)), next);
    }

    [Fact]
    public void ComputeNextRunAt_for_IntervalDays_returns_the_start_date_when_it_is_still_in_the_future()
    {
        var start = new DateOnly(2026, 3, 10);
        var schedule = new Schedule
        {
            Name = "interval",
            ScheduleType = ScheduleType.Recurring,
            Pattern = SchedulePattern.IntervalDays,
            IntervalDays = 3,
            IntervalStartDate = start,
            TimeOfDay = TimeSpan.FromHours(2),
        };
        var after = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.FromHours(5));

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, after, FixedPlusFive);

        Assert.Equal(new DateTimeOffset(2026, 3, 10, 2, 0, 0, TimeSpan.FromHours(5)), next);
    }

    [Fact]
    public void ComputeNextRunAt_for_IntervalDays_advances_by_whole_multiples_of_the_interval()
    {
        var start = new DateOnly(2026, 2, 1);
        var schedule = new Schedule
        {
            Name = "interval",
            ScheduleType = ScheduleType.Recurring,
            Pattern = SchedulePattern.IntervalDays,
            IntervalDays = 3,
            IntervalStartDate = start,
            TimeOfDay = TimeSpan.FromHours(2),
        };
        var after = new DateTimeOffset(2026, 2, 11, 0, 0, 0, TimeSpan.FromHours(5));

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, after, FixedPlusFive);

        Assert.NotNull(next);
        var nextDateInZone = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(next!.Value, FixedPlusFive).Date);
        Assert.Equal(0, (nextDateInZone.DayNumber - start.DayNumber) % 3);
        Assert.True(next.Value >= after);
    }

    [Fact]
    public void ComputeNextRunAt_for_Recurring_with_no_weekly_days_returns_null()
    {
        var schedule = new Schedule
        {
            Name = "broken",
            ScheduleType = ScheduleType.Recurring,
            Pattern = SchedulePattern.Weekly,
            WeeklyDays = null,
            TimeOfDay = TimeSpan.FromHours(1),
        };

        Assert.Null(ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, DateTimeOffset.UtcNow, TimeZoneInfo.Utc));
    }

    [Fact]
    public void ComputeNextRunAt_for_Cron_returns_the_next_matching_minute()
    {
        var after = new DateTimeOffset(2026, 3, 4, 10, 15, 30, TimeSpan.FromHours(5));
        var schedule = new Schedule { Name = "cron", ScheduleType = ScheduleType.Cron, CronExpression = "30 4 * * *" };

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, after, FixedPlusFive);

        Assert.NotNull(next);
        var inZone = TimeZoneInfo.ConvertTime(next!.Value, FixedPlusFive);
        Assert.Equal(4, inZone.Hour);
        Assert.Equal(30, inZone.Minute);
        Assert.True(next.Value >= after);
    }

    [Fact]
    public void ComputeNextRunAt_for_Cron_uses_the_given_time_zone_and_honors_its_own_DST_rules()
    {
        // Reproduces the exact real-world bug this feature fixed: a
        // Recurring/Cron schedule's wall-clock time must resolve to a
        // DIFFERENT UTC instant across a DST boundary for the SAME
        // configured time zone — using TimeZoneInfo.Local (the server
        // process's own, often-UTC-in-a-container OS zone) instead of an
        // explicit admin-configured one, as this class used to, could
        // never do this correctly for an admin in a real DST-observing
        // zone like Europe/Berlin.
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var schedule = new Schedule { Name = "cron", ScheduleType = ScheduleType.Cron, CronExpression = "30 4 * * *" };

        // Winter: CET is UTC+1, so 04:30 Berlin time is 03:30 UTC.
        var winterAfter = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var winterNext = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, winterAfter, berlin);
        Assert.Equal(new DateTimeOffset(2026, 1, 10, 3, 30, 0, TimeSpan.Zero), winterNext);

        // Summer: CEST is UTC+2, so the identical 04:30 Berlin-time
        // schedule is 02:30 UTC instead — a different UTC instant for the
        // same wall-clock rule.
        var summerAfter = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
        var summerNext = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, summerAfter, berlin);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 2, 30, 0, TimeSpan.Zero), summerNext);
    }

    [Fact]
    public void ComputeNextRunAt_for_Cron_with_an_invalid_expression_returns_null()
    {
        var schedule = new Schedule { Name = "broken-cron", ScheduleType = ScheduleType.Cron, CronExpression = "not a cron expression" };

        Assert.Null(ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, DateTimeOffset.UtcNow, TimeZoneInfo.Utc));
    }

    [Fact]
    public void ComputeNextRunAt_for_Cron_with_no_expression_returns_null()
    {
        var schedule = new Schedule { Name = "empty-cron", ScheduleType = ScheduleType.Cron, CronExpression = null };

        Assert.Null(ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, DateTimeOffset.UtcNow, TimeZoneInfo.Utc));
    }

    [Fact]
    public void TryParseCron_returns_false_with_an_error_message_for_an_invalid_expression()
    {
        var parsed = ScheduleRecurrenceCalculator.TryParseCron("not a cron expression", out _, out var error);

        Assert.False(parsed);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParseCron_returns_true_for_a_valid_standard_five_field_expression()
    {
        var parsed = ScheduleRecurrenceCalculator.TryParseCron("*/15 * * * *", out var cron, out var error);

        Assert.True(parsed);
        Assert.NotNull(cron);
        Assert.Null(error);
    }

    [Fact]
    public void FormatWeeklyDays_and_ParseWeeklyDays_round_trip()
    {
        IReadOnlyList<DayOfWeek> days = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];

        var formatted = ScheduleRecurrenceCalculator.FormatWeeklyDays(days);
        var parsed = ScheduleRecurrenceCalculator.ParseWeeklyDays(formatted);

        Assert.Equal(days, parsed);
    }
}
