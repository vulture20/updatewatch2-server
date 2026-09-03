namespace UpdateWatch2.Server;

/// <summary>
/// SemVer version of the server itself, independent of the agent, protocol,
/// and DB schema versions (see CLAUDE.md). Keep in sync with the repository
/// root VERSION file; bump both together.
/// </summary>
public static class AppVersion
{
    public const string Current = "0.1.0";
}
