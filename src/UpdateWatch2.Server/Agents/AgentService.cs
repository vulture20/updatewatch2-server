using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.UpdateFilters;

namespace UpdateWatch2.Server.Agents;

public class AgentService(AppDbContext db, IAuditLogService auditLog) : IAgentService
{
    public async Task<IReadOnlyList<AgentListItemDto>> GetAllAsync(CancellationToken ct = default)
    {
        var agents = await db.Agents
            .OrderBy(a => a.Hostname)
            .Select(a => new { a.Id, a.Hostname, a.Approved, a.RebootRequired })
            .ToListAsync(ct);

        var countsByAgent = await CountFilteredPendingUpdatesByAgentAsync(ct);

        return agents
            .Select(a => new AgentListItemDto(a.Hostname, a.Approved, a.RebootRequired, countsByAgent.GetValueOrDefault(a.Id)))
            .ToList();
    }

    public async Task<AgentDetailDto?> GetByHostnameAsync(string hostname, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return null;
        }

        var countsByAgent = await CountFilteredPendingUpdatesByAgentAsync(ct, onlyAgentId: agent.Id);

        return new AgentDetailDto(
            agent.Hostname, agent.DnsName, agent.OperatingSystem, agent.IpAddress, agent.AgentVersion,
            agent.Approved, agent.RebootRequired, countsByAgent.GetValueOrDefault(agent.Id), agent.LastAliveAt,
            agent.ClientCertificateThumbprint, agent.ClientCertificateThumbprintSha1, agent.ClientCertificateIssuedAt, agent.ClientCertificateExpiresAt,
            agent.PendingInstallRequestedAt, agent.LastInstallOutcome, agent.LastInstallCompletedAt);
    }

    /// <summary>
    /// Pending-update count per agent, excluding anything an active
    /// <see cref="Db.Entities.UpdateFilter"/> matches — computed live on
    /// every call rather than trusted from the raw, unfiltered
    /// <see cref="Db.Entities.Agent.PendingUpdateCount"/> column, so
    /// adding/editing/deleting a filter changes what's displayed
    /// immediately, with no need to wait for the agent's next report.
    /// <paramref name="onlyAgentId"/> scopes the underlying query to one
    /// agent (the detail page) instead of loading every agent's items (the
    /// overview list) — either way the filter list itself is only fetched
    /// once.
    /// </summary>
    private async Task<Dictionary<int, int>> CountFilteredPendingUpdatesByAgentAsync(CancellationToken ct, int? onlyAgentId = null)
    {
        var filters = await db.UpdateFilters.ToListAsync(ct);

        var itemsQuery = db.UpdateItems.AsQueryable();
        if (onlyAgentId is not null)
        {
            itemsQuery = itemsQuery.Where(u => u.AgentId == onlyAgentId);
        }

        var items = await itemsQuery.Select(u => new { u.AgentId, u.Title }).ToListAsync(ct);

        return items
            .Where(u => !UpdateFilterMatcher.IsExcluded(u.Title, filters))
            .GroupBy(u => u.AgentId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

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
        agent.IssuingRootThumbprint = null;

        var (rawToken, hash) = RegistrationTokenHasher.GenerateToken();
        agent.RegistrationTokenHash = hash;

        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(initiatedBy, "agent.certificate.reissue", hostname, ct);

        return ReissueCertificateResult.Succeeded(rawToken);
    }

    public async Task<bool> DeleteAsync(string hostname, string initiatedBy, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        db.Agents.Remove(agent);
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(initiatedBy, "agent.delete", hostname, ct);

        return true;
    }

    // Capped so a large fleet's confirm dialog/admin panel never has to
    // render an unbounded list — StillOnPreviousRootCount still reports the
    // true total even when the hostname list itself is truncated.
    private const int MaxAffectedHostnamesReturned = 20;

    public async Task<CaRotationImpactDto> GetCaRotationImpactAsync(string? previousRootThumbprintSha256, CancellationToken ct = default)
    {
        if (previousRootThumbprintSha256 is null)
        {
            return CaRotationImpactDto.None;
        }

        var stillOnPreviousRoot = db.Agents.Where(a => a.IssuingRootThumbprint == previousRootThumbprintSha256);
        var count = await stillOnPreviousRoot.CountAsync(ct);
        var hostnames = await stillOnPreviousRoot
            .OrderBy(a => a.Hostname)
            .Take(MaxAffectedHostnamesReturned)
            .Select(a => a.Hostname)
            .ToListAsync(ct);

        var unknownRootCount = await db.Agents
            .CountAsync(a => a.ClientCertificateThumbprint != null && a.IssuingRootThumbprint == null, ct);

        return new CaRotationImpactDto(count, hostnames, unknownRootCount);
    }
}
