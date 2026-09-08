namespace UpdateWatch2.Server.Certificates;

/// <summary>
/// Why a presented agent client certificate was rejected — shared between
/// <see cref="ICertificateValidator"/> (a cryptographically valid,
/// CA-chained certificate that just doesn't belong to a known/approved
/// agent) and <c>Program.cs</c>'s <c>OnAuthenticationFailed</c> handler (the
/// certificate itself failed chain/validity-period validation, so
/// <see cref="ICertificateValidator"/> is never even reached). Classified
/// explicitly from the certificate's own <c>NotBefore</c>/<c>NotAfter</c>
/// rather than parsed out of a raw exception message, so both the log line
/// and the admin UI show one of a small, stable, translatable set of
/// reasons instead of whatever .NET's X509Chain happened to phrase that
/// day.
/// </summary>
public enum CertificateRejectionReason
{
    Expired,
    NotYetValid,

    /// <summary>Doesn't chain to a currently trusted internal CA root, or fails some other structural/usage check (e.g. wrong key usage).</summary>
    NotTrusted,

    /// <summary>Chain-valid and CA-signed, but no <see cref="Db.Entities.Agent"/> row has this thumbprint.</summary>
    UnknownAgent,

    /// <summary>Chain-valid, CA-signed, and matches a known agent — but that agent isn't (or is no longer) approved.</summary>
    AgentNotApproved,
}
