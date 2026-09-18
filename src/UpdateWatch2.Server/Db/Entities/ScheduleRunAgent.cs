using System.Text.Json.Serialization;

namespace UpdateWatch2.Server.Db.Entities;

/// <summary>JsonStringEnumConverter-decorated — see <see cref="ScheduleType"/>'s own doc comment for why.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScheduleRunActionStatus
{
    /// <summary>This run's schedule didn't request this action at all (e.g. <c>RebootStatus</c> on an install-only schedule).</summary>
    NotApplicable,

    /// <summary>"Only if required" was checked and, when evaluated, no reboot was actually needed — no trigger was ever sent.</summary>
    Skipped,

    /// <summary>
    /// Reboot-only status: install completed (or this run has no install
    /// action) but "only if required" is still waiting to see whether the
    /// agent reports <see cref="Agent.RebootRequired"/> before this run's
    /// deadline — see <see cref="Agents.AgentRegistrationService.RecordAliveAsync"/>.
    /// </summary>
    AwaitingInstallResult,

    /// <summary>Trigger sent, waiting for the agent to pick it up on a heartbeat and acknowledge.</summary>
    Pending,

    /// <summary>Agent acknowledged with a successful outcome.</summary>
    Delivered,

    /// <summary>Deadline passed before the agent ever acknowledged (offline, or never reached <c>AwaitingInstallResult</c>'s resolution in time).</summary>
    Missed,

    /// <summary>Agent acknowledged with a failed outcome — see <see cref="ErrorDetail"/>.</summary>
    Failed,
}

/// <summary>One target agent's outcome within a single <see cref="ScheduleRun"/>.</summary>
public class ScheduleRunAgent
{
    public int ScheduleRunId { get; set; }

    public ScheduleRun? ScheduleRun { get; set; }

    public required string Hostname { get; set; }

    public ScheduleRunActionStatus InstallStatus { get; set; }

    public ScheduleRunActionStatus RebootStatus { get; set; }

    /// <summary>Only ever meaningful alongside a <c>Failed</c> status — mirrors <see cref="Agent.LastInstallErrorDetail"/>'s convention.</summary>
    public string? ErrorDetail { get; set; }
}
