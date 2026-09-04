namespace UpdateWatch2.Server.Certificates;

/// <summary>
/// Shared names for the client-certificate authentication scheme/policy,
/// referenced from both Program.cs's <c>AddAuthentication</c>/
/// <c>AddAuthorization</c> wiring and the controllers that gate agent-facing
/// routes on it — kept in one place so they can't silently drift apart.
/// </summary>
public static class CertificateAuthenticationSetup
{
    public const string SchemeName = "AgentClientCertificate";

    public const string AgentCertificatePolicy = "AgentCertificate";
}
