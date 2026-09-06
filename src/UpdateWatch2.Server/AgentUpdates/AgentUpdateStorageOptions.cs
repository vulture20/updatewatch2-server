namespace UpdateWatch2.Server.AgentUpdates;

/// <summary>
/// Resolved filesystem path where downloaded agent release assets are
/// cached. Computed once in Program.cs the exact same way
/// <c>Certs:Path</c>/<c>Database:Path</c> already are (a relative
/// appsettings.json path resolved against <c>ContentRootPath</c>) rather
/// than bound as an <c>IOptions&lt;T&gt;</c> — see those two paths' own
/// comments in Program.cs for why that resolution has to happen eagerly,
/// before any request-scoped code runs.
/// </summary>
public record AgentUpdateStorageOptions(string Path);
