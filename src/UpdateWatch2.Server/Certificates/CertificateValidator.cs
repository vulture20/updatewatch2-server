using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Db;

namespace UpdateWatch2.Server.Certificates;

public class CertificateValidator(AppDbContext db) : ICertificateValidator
{
    public async Task<CertificateValidationResult> ValidateAsync(X509Certificate2 certificate, CancellationToken ct = default)
    {
        var thumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);

        var agent = await db.Agents.SingleOrDefaultAsync(a => a.ClientCertificateThumbprint == thumbprint, ct);
        if (agent is null)
        {
            return CertificateValidationResult.Failed("Certificate does not match any known agent.");
        }

        if (!agent.Approved)
        {
            // Shouldn't normally happen — a thumbprint is only ever recorded
            // once an admin has approved the agent (see
            // AgentRegistrationService) — but an admin could in principle
            // revoke approval after the fact, so this is checked explicitly
            // rather than assumed from the thumbprint's mere presence.
            return CertificateValidationResult.Failed($"Agent '{agent.Hostname}' is not approved.");
        }

        return CertificateValidationResult.Succeeded(agent.Hostname);
    }
}
