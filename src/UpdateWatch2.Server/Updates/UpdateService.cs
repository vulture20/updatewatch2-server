using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Updates;

public class UpdateService(AppDbContext db, IAuditLogService auditLog) : IUpdateService
{
    public async Task<IReadOnlyList<UpdateItemDto>?> GetForAgentAsync(string hostname, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return null;
        }

        return await db.UpdateItems
            .Where(u => u.AgentId == agent.Id)
            .OrderBy(u => u.Title)
            .Select(u => new UpdateItemDto(u.Id, u.Title, u.PackageId, u.Description, u.DetectedAt, u.Installed))
            .ToListAsync(ct);
    }

    public async Task<bool> ReportUpdatesAsync(string hostname, ReportUpdatesRequest report, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        var existing = await db.UpdateItems.Where(u => u.AgentId == agent.Id).ToListAsync(ct);
        db.UpdateItems.RemoveRange(existing);

        foreach (var update in report.Updates)
        {
            db.UpdateItems.Add(new UpdateItem
            {
                AgentId = agent.Id,
                Title = update.Title,
                PackageId = update.PackageId,
                Description = update.Description,
            });
        }

        agent.PendingUpdateCount = report.Updates.Count;
        agent.RebootRequired = report.RebootRequired;
        agent.LastAliveAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> TriggerInstallAsync(string hostname, string triggeredBy, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        // Delivery is the agent's own alive-heartbeat poll picking this up
        // (updatewatch2-server#10/updatewatch2-agent#4) — see
        // AgentRegistrationService.RecordAliveAsync and
        // AgentProtocolController.Alive.
        agent.PendingInstallRequestedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(triggeredBy, "updates.install.trigger", hostname, ct);
        return true;
    }

    public async Task<bool> AcknowledgeInstallAsync(string hostname, InstallOutcome outcome, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        agent.PendingInstallRequestedAt = null;
        agent.LastInstallOutcome = outcome.ToString();
        agent.LastInstallCompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync("agent", $"updates.install.{outcome.ToString().ToLowerInvariant()}", hostname, ct);
        return true;
    }
}
