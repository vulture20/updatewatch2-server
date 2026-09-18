using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Schedules;

namespace UpdateWatch2.Server.Tests.Schedules;

public class ScheduleRecurrenceCalculatorTests
{
    [Fact]
    public void ComputeNextRunAt_for_a_future_Once_schedule_returns_its_own_date()
    {
        var onceAt = DateTimeOffset.UtcNow.AddDays(3);
        var schedule = new Schedule { Name = "once", ScheduleType = ScheduleType.Once, OnceAt = onceAt };

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, DateTimeOffset.UtcNow);

        Assert.Equal(onceAt, next);
    }

    [Fact]
    public void ComputeNextRunAt_for_an_Once_schedule_after_it_already_fired_returns_null()
    {
        var onceAt = DateTimeOffset.UtcNow.AddDays(-1);
        var schedule = new Schedule { Name = "once", ScheduleType = ScheduleType.Once, OnceAt = onceAt };

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, DateTimeOffset.UtcNow);

        Assert.Null(next);
    }

    [Fact]
    public void ComputeNextRunAt_for_Weekly_picks_the_earliest_matching_weekday_at_or_after_now()
    {
        // A Wednesday, well clear of any DST boundary.
        var wednesday = new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero).ToLocalTime();
        var schedule = new Schedule
        {
            Name = "weekly",
            ScheduleType = ScheduleType.Recurring,
            Pattern = SchedulePattern.Weekly,
            WeeklyDays = ScheduleRecurrenceCalculator.FormatWeeklyDays([DayOfWeek.Monday, DayOfWeek.Friday]),
            TimeOfDay = TimeSpan.FromHours(14),
        };

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, wednesday);

        Assert.NotNull(next);
        Assert.Equal(DayOfWeek.Friday, next!.Value.ToLocalTime().DayOfWeek);
        Assert.Equal(TimeSpan.FromHours(14), next.Value.ToLocalTime().TimeOfDay);
    }

    [Fact]
    public void ComputeNextRunAt_for_Weekly_wraps_to_next_week_once_todays_time_has_passed()
    {
        var mondayEvening = new DateTimeOffset(2026, 3, 2, 20, 0, 0, TimeSpan.Zero).ToLocalTime();
        var schedule = new Schedule
        {
            Name = "weekly",
            ScheduleType = ScheduleType.Recurring,
            Pattern = SchedulePattern.Weekly,
            WeeklyDays = ScheduleRecurrenceCalculator.FormatWeeklyDays([DayOfWeek.Monday]),
            TimeOfDay = TimeSpan.FromHours(9),
        };

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, mondayEvening);

        Assert.NotNull(next);
        Assert.Equal(DayOfWeek.Monday, next!.Value.ToLocalTime().DayOfWeek);
        Assert.True(next.Value > mondayEvening);
        Assert.Equal(7, (next.Value.ToLocalTime().Date - mondayEvening.ToLocalTime().Date).Days);
    }

    [Fact]
    public void ComputeNextRunAt_for_IntervalDays_returns_the_start_date_when_it_is_still_in_the_future()
    {
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(5));
        var schedule = new Schedule
        {
            Name = "interval",
            ScheduleType = ScheduleType.Recurring,
            Pattern = SchedulePattern.IntervalDays,
            IntervalDays = 3,
            IntervalStartDate = start,
            TimeOfDay = TimeSpan.FromHours(2),
        };

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, DateTimeOffset.UtcNow);

        Assert.Equal(start, DateOnly.FromDateTime(next!.Value.ToLocalTime().Date));
    }

    [Fact]
    public void ComputeNextRunAt_for_IntervalDays_advances_by_whole_multiples_of_the_interval()
    {
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-10));
        var schedule = new Schedule
        {
            Name = "interval",
            ScheduleType = ScheduleType.Recurring,
            Pattern = SchedulePattern.IntervalDays,
            IntervalDays = 3,
            IntervalStartDate = start,
            TimeOfDay = TimeSpan.FromHours(2),
        };

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, DateTimeOffset.UtcNow);

        Assert.NotNull(next);
        var daysSinceStart = DateOnly.FromDateTime(next!.Value.ToLocalTime().Date).DayNumber - start.DayNumber;
        Assert.Equal(0, daysSinceStart % 3);
        Assert.True(next.Value >= DateTimeOffset.UtcNow);
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

        Assert.Null(ScheduleRecurrenceCalculator.ComputeNextRunAt(schedule, DateTimeOffset.UtcNow));
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
