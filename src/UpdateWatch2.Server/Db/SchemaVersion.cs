namespace UpdateWatch2.Server.Db;

/// <summary>
/// SemVer version of the database schema, independent of the server, agent,
/// and protocol versions (see CLAUDE.md "Four independent version numbers").
/// Bump on any migration that changes the schema.
/// </summary>
public static class SchemaVersion
{
    public const string Current = "0.3.0";
}
