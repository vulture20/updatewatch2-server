namespace UpdateWatch2.Server.Demo;

/// <summary>
/// Seeds a handful of realistic-looking dummy agents (and their pending
/// updates) so an otherwise-empty instance is demonstrable — gated behind
/// the <c>UPDATEWATCH2_DEMOMODE</c> environment variable (see Program.cs),
/// never an admin-UI setting, matching how <c>UPDATEWATCH2_TRUSTEDIP</c> is
/// also deliberately env-var-only (CLAUDE.md).
/// </summary>
public interface IDemoDataSeeder
{
    Task EnsureSeededAsync(CancellationToken ct = default);
}
