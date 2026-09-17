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

    public static bool IsValid(UpdateAgentSettingsRequest request) =>
        IsValidLogLevel(request.DesiredLogLevel)
        && IsValidUpdateCheckIntervalMinutes(request.DesiredUpdateCheckIntervalMinutes)
        && IsValidUpdateCheckJitterSeconds(request.DesiredUpdateCheckJitterSeconds);

    public static bool IsValidLogLevel(string value) =>
        AllowedLogLevels.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static bool IsValidUpdateCheckIntervalMinutes(int value) =>
        value >= MinIntervalMinutes && value <= MaxIntervalMinutes;

    public static bool IsValidUpdateCheckJitterSeconds(int value) =>
        value >= MinJitterSeconds && value <= MaxJitterSeconds;
}
