namespace UpdateWatch2.Server.Admin;

/// <summary>
/// Maps this project's own DEBUG/INFO/WARNING/ERROR log-level vocabulary
/// (<see cref="AdminSettings.LogLevel"/>, <c>UPDATEWATCH2_LOGLEVEL</c>) to
/// the <see cref="Microsoft.Extensions.Logging.LogLevel"/> enum name
/// <c>Logging:LogLevel:Default</c> actually expects — shared by
/// <c>Program.cs</c>'s pre-DI startup resolution and
/// <see cref="AdminSettingsStore"/>'s own live re-application (see its
/// doc comment on why the same mapping now runs from two call sites).
/// </summary>
public static class LogLevelMapper
{
    /// <summary>
    /// The two <c>Microsoft.Extensions.Http</c> logging categories
    /// <c>AddHttpClient&lt;IGitHubReleaseClient, GitHubReleaseClient&gt;</c>
    /// (<c>Program.cs</c>) picks up automatically — "Start/End processing
    /// HTTP request", "Sending HTTP request"/"Received HTTP response
    /// headers", and "HTTP request failed" (on a genuine transport
    /// failure). These are hardcoded at Information severity by that
    /// NuGet package itself, so unlike this project's own log calls their
    /// level can't just be edited at the source — see
    /// <see cref="ToHttpClientLoggingCategoryValue"/> for how this project
    /// demotes them to Debug-only visibility instead (log-level-audit.md).
    /// </summary>
    public const string GitHubReleaseClientLogicalHandlerCategory = "System.Net.Http.HttpClient.IGitHubReleaseClient.LogicalHandler";
    public const string GitHubReleaseClientClientHandlerCategory = "System.Net.Http.HttpClient.IGitHubReleaseClient.ClientHandler";

    public static string ToConfigurationValue(string value) => value.Trim().ToUpperInvariant() switch
    {
        "DEBUG" => nameof(Microsoft.Extensions.Logging.LogLevel.Debug),
        "INFO" => nameof(Microsoft.Extensions.Logging.LogLevel.Information),
        "WARNING" => nameof(Microsoft.Extensions.Logging.LogLevel.Warning),
        "ERROR" => nameof(Microsoft.Extensions.Logging.LogLevel.Error),
        _ => value,
    };

    public static bool IsValid(string value) =>
        Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(ToConfigurationValue(value), out _);

    /// <summary>
    /// A category-specific override value for the two chatty
    /// <c>Microsoft.Extensions.Http</c> categories above: a plain
    /// <c>Logging:LogLevel:Default</c> can only ever raise or lower the
    /// MINIMUM level a category logs at, never change an individual
    /// message's own hardcoded severity — so an Information-level library
    /// message can't be "made Debug" the way this project's own log calls
    /// can. Instead, the category itself is gated: only when the
    /// effective level is genuinely DEBUG does this return "Debug" (letting
    /// the Information-level messages through, same as every other
    /// category at that setting); every other selectable level (INFO,
    /// WARNING, ERROR) returns "Warning" instead of "Information", which
    /// suppresses them — nothing in either category ever logs above
    /// Information, so "Warning" as a floor is a complete, not partial,
    /// mute. Called from both Program.cs's pre-Build() resolution and
    /// AdminSettingsStore.Apply's live re-application, exactly like
    /// ToConfigurationValue's own two call sites.
    /// </summary>
    public static string ToHttpClientLoggingCategoryValue(string value) =>
        ToConfigurationValue(value) == nameof(Microsoft.Extensions.Logging.LogLevel.Debug)
            ? nameof(Microsoft.Extensions.Logging.LogLevel.Debug)
            : nameof(Microsoft.Extensions.Logging.LogLevel.Warning);
}
