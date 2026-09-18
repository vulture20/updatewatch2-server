using System.Text.Json.Serialization;
using UpdateWatch2.Server.Api;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Schedules;

/// <summary>
/// Computed, not stored — <c>Completed</c> means a <see cref="ScheduleType.Once"/>
/// schedule's <see cref="Schedule.NextRunAt"/> has become null (it fired),
/// regardless of whether its individual agents' outcomes are all resolved
/// yet (see the run history for that detail). <c>Paused</c> takes priority
/// over <c>Completed</c> if somehow both apply (shouldn't normally happen,
/// since a completed Once schedule is rarely also disabled).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScheduleStatus
{
    Active,
    Paused,
    Completed,
}

public record ScheduleDto(
    int Id,
    string Name,
    bool Enabled,
    ScheduleStatus Status,
    ScheduleType ScheduleType,
    SchedulePattern? Pattern,
    DateTimeOffset? OnceAt,
    // Plain day-name strings ("Monday", ...), not System.DayOfWeek
    // directly — a BCL enum can't carry this codebase's own
    // JsonStringEnumConverter attribute, and registering that converter
    // globally would silently change every OTHER enum's wire encoding
    // too. ScheduleService converts to/from DayOfWeek internally.
    IReadOnlyList<string>? WeeklyDays,
    TimeSpan TimeOfDay,
    int? IntervalDays,
    DateOnly? IntervalStartDate,
    string? CronExpression,
    bool ActionInstall,
    bool ActionReboot,
    bool RebootOnlyIfRequired,
    int DeadlineHours,
    bool NotifyOnFailure,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    IReadOnlyList<string> Hostnames);

/// <summary>Body of both the create and the update endpoint — same editable fields either way, mirroring <c>UpsertUpdateFilterRequest</c>'s convention.</summary>
public record UpsertScheduleRequest(
    string Name,
    bool Enabled,
    ScheduleType ScheduleType,
    SchedulePattern? Pattern,
    DateTimeOffset? OnceAt,
    IReadOnlyList<string>? WeeklyDays,
    TimeSpan TimeOfDay,
    int? IntervalDays,
    DateOnly? IntervalStartDate,
    string? CronExpression,
    bool ActionInstall,
    bool ActionReboot,
    bool RebootOnlyIfRequired,
    int DeadlineHours,
    bool NotifyOnFailure,
    IReadOnlyList<string> Hostnames);

/// <summary>Result of a create/update — mirrors <c>UpdateFilterResult</c>'s success/failure-reason shape.</summary>
public record ScheduleResult(bool Success, ScheduleDto? Schedule, string? FailureReason, ApiErrorCode? ErrorCode = null, string? ErrorDetail = null)
{
    public static ScheduleResult Failed(string reason, ApiErrorCode? errorCode = null, string? errorDetail = null) => new(false, null, reason, errorCode, errorDetail);

    public static ScheduleResult Succeeded(ScheduleDto schedule) => new(true, schedule, null);
}

public record ScheduleRunAgentDto(string Hostname, ScheduleRunActionStatus InstallStatus, ScheduleRunActionStatus RebootStatus, string? ErrorDetail);

public record ScheduleRunDto(int Id, DateTimeOffset FiredAt, DateTimeOffset DeadlineAt, bool ActionInstall, bool ActionReboot, bool RebootOnlyIfRequired, IReadOnlyList<ScheduleRunAgentDto> Agents);
