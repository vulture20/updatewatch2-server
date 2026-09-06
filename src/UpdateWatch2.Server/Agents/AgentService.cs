using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;

namespace UpdateWatch2.Server.Agents;

public class AgentService(AppDbContext db, IAuditLogService auditLog) : IAgentService
{
    public async Task<IReadOnlyList<AgentListItemDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Agents
            .OrderBy(a => a.Hostname)
            .Select(a => new AgentListItemDto(a.Hostname, a.Approved, a.RebootRequired, a.PendingUpdateCount))
            .ToListAsync(ct);

    public async Task<AgentDetailDto?> GetByHostnameAsync(string hostname, CancellationToken ct = default) =>
        await db.Agents
            .Where(a => a.Hostname == hostname)
            .Select(a => new AgentDetailDto(
                a.Hostname, a.DnsName, a.OperatingSystem, a.IpAddress, a.AgentVersion,
                a.Approved, a.RebootRequired, a.PendingUpdateCount, a.LastAliveAt,
                a.ClientCertificateThumbprint, a.ClientCertificateThumbprintSha1, a.ClientCertificateIssuedAt, a.ClientCertificateExpiresAt,
                a.PendingInstallRequestedAt, a.LastInstallOutcome, a.LastInstallCompletedAt))
            .SingleOrDefaultAsync(ct);

    public async Task<bool> ApproveAsync(string hostname, string approvedBy, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        agent.Approved = true;
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(approvedBy, "agent.approve", hostname, ct);
        return true;
    }

    public async Task<BulkApproveResult> ApproveManyAsync(IReadOnlyList<string> hostnames, string approvedBy, CancellationToken ct = default)
    {
        var agents = await db.Agents.Where(a => hostnames.Contains(a.Hostname)).ToListAsync(ct);
        foreach (var agent in agents)
        {
            agent.Approved = true;
        }

        await db.SaveChangesAsync(ct);

        var approvedHostnames = agents.Select(a => a.Hostname).ToHashSet();
        var notFound = hostnames.Where(h => !approvedHostnames.Contains(h)).ToList();

        await auditLog.LogAsync(approvedBy, "agent.approve.bulk", string.Join(", ", approvedHostnames), ct);

        return new BulkApproveResult(agents.Count, notFound);
    }

    public async Task<ReissueCertificateResult> ReissueCertificateAsync(string hostname, string initiatedBy, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return ReissueCertificateResult.Failed("Agent not found.");
        }

        if (!agent.Approved)
        {
            // Never had a certificate to lose — guide the admin toward
            // Approve instead of handing back a confusing/misleading token.
            return ReissueCertificateResult.Failed("Agent is not approved.");
        }

        agent.ClientCertificateThumbprint = null;
        agent.ClientCertificateThumbprintSha1 = null;
        agent.ClientCertificateIssuedAt = null;
        agent.ClientCertificateExpiresAt = null;

        var (rawToken, hash) = RegistrationTokenHasher.GenerateToken();
        agent.RegistrationTokenHash = hash;

        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(initiatedBy, "agent.certificate.reissue", hostname, ct);

        return ReissueCertificateResult.Succeeded(rawToken);
    }
}
