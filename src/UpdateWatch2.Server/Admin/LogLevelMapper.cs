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
}
