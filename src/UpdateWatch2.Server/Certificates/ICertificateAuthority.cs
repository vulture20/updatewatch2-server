using System.Security.Cryptography.X509Certificates;

namespace UpdateWatch2.Server.Certificates;

/// <summary>Result of issuing a new agent client certificate.</summary>
public record IssuedCertificate(byte[] PfxBytes, string ThumbprintSha256, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// UpdateWatch2's internal, self-signed certificate authority — the root of
/// trust for agent-server mutual TLS (see CLAUDE.md "Certificate-based
/// mutual auth is the security backbone"). No external CA/ACME integration;
/// see the class-level doc comment on <see cref="InternalCertificateAuthority"/>
/// for why, and for the follow-up issues this deliberately leaves open
/// (root rotation, leaf renewal).
/// </summary>
public interface ICertificateAuthority
{
    /// <summary>
    /// The root CA certificate (public + private key, in-memory only — never
    /// sent to a client as-is). Its public half is what
    /// <c>GET /api/agent/ca-certificate</c> hands out, and what both the
    /// server's cert-auth middleware and agents pin as their trust anchor.
    /// </summary>
    X509Certificate2 RootCertificate { get; }

    /// <summary>
    /// Loads the server's own TLS leaf certificate (the one Kestrel presents
    /// on the agent-facing port), generating or regenerating it as needed so
    /// its SAN always matches <paramref name="sanHostname"/> — see
    /// <see cref="InternalCertificateAuthority"/>'s remarks on why a mismatch
    /// triggers silent regeneration rather than requiring a manual step.
    /// </summary>
    X509Certificate2 EnsureServerLeaf(string sanHostname);

    /// <summary>
    /// Issues a brand-new client certificate for an approved agent, signed by
    /// <see cref="RootCertificate"/>, Subject CN = <paramref name="hostname"/>,
    /// Enhanced Key Usage = Client Authentication only, valid for
    /// <paramref name="validity"/> from issuance (updatewatch2-server#9 —
    /// the caller is responsible for sourcing this, typically from the
    /// live admin-configured <c>CertificateOptions.AgentCertificateValidityDays</c>;
    /// this class deliberately stays unaware of admin settings entirely).
    /// </summary>
    IssuedCertificate IssueAgentLeaf(string hostname, TimeSpan validity);
}
