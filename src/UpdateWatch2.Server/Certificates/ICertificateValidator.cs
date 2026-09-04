using System.Security.Cryptography.X509Certificates;

namespace UpdateWatch2.Server.Certificates;

public record CertificateValidationResult(bool Success, string? Hostname, string? FailureReason)
{
    public static CertificateValidationResult Failed(string reason) => new(false, null, reason);

    public static CertificateValidationResult Succeeded(string hostname) => new(true, hostname, null);
}

/// <summary>
/// Maps an already chain-validated client certificate (the cert-auth
/// middleware has already confirmed it chains to our internal CA and hasn't
/// expired — see Program.cs's <c>AddCertificate</c> setup) to the
/// <see cref="Db.Entities.Agent"/> it belongs to. This is the "does the
/// server actually recognize and still trust this specific agent" check on
/// top of "is this cert cryptographically legitimate".
/// </summary>
public interface ICertificateValidator
{
    Task<CertificateValidationResult> ValidateAsync(X509Certificate2 certificate, CancellationToken ct = default);
}
