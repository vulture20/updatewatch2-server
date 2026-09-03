namespace UpdateWatch2.Server.Protocol;

/// <summary>
/// SemVer version of the agent-server transfer protocol, independent of the
/// server, agent, and DB schema versions (see CLAUDE.md). Bump on any
/// change to request/response shapes so mismatched agent/server builds can
/// detect incompatibility.
/// </summary>
public static class ProtocolVersion
{
    public const string Current = "0.1.0";
}
