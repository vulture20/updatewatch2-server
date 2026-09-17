namespace UpdateWatch2.Server.Agents;

/// <summary>
/// Validates the body of <c>PUT /api/agents/{hostname}/settings</c>
/// (<see cref="UpdateAgentSettingsRequest"/>) before it's ever persisted as
/// an override an agent will unconditionally enforce — an admin-facing
/// route behind the normal cookie session, but a rejected value here still
/// stops a typo (e.g. a negative interval) from being pushed down and
/// applied on the agent's very next heartbeat with no server-side chance to
/// catch it afterward.
/// </summary>
public static class AgentSettingsValidator
{
    private static readonly string[] AllowedLogLevels = ["DEBUG", "INFO", "WARNING", "ERROR"];

    // Generous bounds matching AgentOptions' own defaults/intent
    // (240 minutes/300 seconds) rather than the tightest plausible range —
    // this only guards against an obvious typo, not a policy decision
    // about what interval is "reasonable".
    private const int MinIntervalMinutes = 1;
    private const int MaxIntervalMinutes = 10_080; // one week
    private const int MinJitterSeconds = 0;
    private const int MaxJitterSeconds = 3_600; // one hour

    // Deliberately a much tighter guard than MaxIntervalMinutes above —
    // this is the alive-heartbeat cadence itself, not a periodic update
    // check: install/reboot triggers, self-update offers, certificate
    // renewal/rotation checks, and the online/offline threshold all ride
    // this same interval, so a typo that stretches it out to (say) a week
    // would degrade far more than just how often updates are searched for.
    // Still just an anti-typo guard, not a policy stance on what's
    // "reasonable" for a given deployment.
    private const int MinAliveIntervalMinutes = 1;
    private const int MaxAliveIntervalMinutes = 1_440; // one day

    public static bool IsValid(UpdateAgentSettingsRequest request) =>
        IsValidLogLevel(request.DesiredLogLevel)
        && IsValidUpdateCheckIntervalMinutes(request.DesiredUpdateCheckIntervalMinutes)
        && IsValidUpdateCheckJitterSeconds(request.DesiredUpdateCheckJitterSeconds)
        && IsValidAliveIntervalMinutes(request.DesiredAliveIntervalMinutes);

    /// <summary>
    /// Validates <see cref="BulkUpdateAgentSettingsRequest"/> — each field is
    /// independently optional (null = leave untouched on every selected
    /// agent), but at least one must be provided, and any field that IS
    /// provided still has to pass the same per-field checks the single-agent
    /// request above uses.
    /// </summary>
    public static bool IsValidBulkRequest(BulkUpdateAgentSettingsRequest request) =>
        (request.DesiredLogLevel is not null || request.DesiredUpdateCheckIntervalMinutes is not null
            || request.DesiredUpdateCheckJitterSeconds is not null || request.DesiredAliveIntervalMinutes is not null)
        && (request.DesiredLogLevel is null || IsValidLogLevel(request.DesiredLogLevel))
        && (request.DesiredUpdateCheckIntervalMinutes is null || IsValidUpdateCheckIntervalMinutes(request.DesiredUpdateCheckIntervalMinutes.Value))
        && (request.DesiredUpdateCheckJitterSeconds is null || IsValidUpdateCheckJitterSeconds(request.DesiredUpdateCheckJitterSeconds.Value))
        && (request.DesiredAliveIntervalMinutes is null || IsValidAliveIntervalMinutes(request.DesiredAliveIntervalMinutes.Value));

    public static bool IsValidLogLevel(string value) =>
        AllowedLogLevels.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static bool IsValidUpdateCheckIntervalMinutes(int value) =>
        value >= MinIntervalMinutes && value <= MaxIntervalMinutes;

    public static bool IsValidUpdateCheckJitterSeconds(int value) =>
        value >= MinJitterSeconds && value <= MaxJitterSeconds;

    public static bool IsValidAliveIntervalMinutes(int value) =>
        value >= MinAliveIntervalMinutes && value <= MaxAliveIntervalMinutes;
}
