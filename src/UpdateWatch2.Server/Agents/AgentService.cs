using Microsoft.EntityFrameworkCore;
using UpdateWatch2.Server.Admin;
using UpdateWatch2.Server.Api;
using UpdateWatch2.Server.Audit;
using UpdateWatch2.Server.Certificates;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.UpdateFilters;

namespace UpdateWatch2.Server.Agents;

public class AgentService(
    AppDbContext db,
    IAuditLogService auditLog,
    ICertificateRejectionService rejectionService,
    IAdminSettingsStore settingsStore) : IAgentService
{
    public async Task<IReadOnlyList<AgentListItemDto>> GetAllAsync(CancellationToken ct = default)
    {
        var agents = await db.Agents
            .OrderBy(a => a.Hostname)
            .Select(a => new
            {
                a.Id, a.Hostname, a.Approved, a.RebootRequired, a.LastAliveAt, a.OperatingSystem,
                a.PendingInstallRequestedAt, a.PendingRebootRequestedAt,
            })
            .ToListAsync(ct);

        var countsByAgent = await CountFilteredPendingUpdatesByAgentAsync(ct);
        var rejectionsByHostname = await rejectionService.GetRecentByHostnameAsync(ct);
        var offlineThreshold = TimeSpan.FromMinutes(settingsStore.AgentOffline.ThresholdMinutes);

        return agents
            .Select(a => new AgentListItemDto(
                a.Hostname, a.Approved, a.RebootRequired, countsByAgent.GetValueOrDefault(a.Id),
                ResolveActiveRejection(rejectionsByHostname.GetValueOrDefault(a.Hostname), a.LastAliveAt)?.Reason,
                a.OperatingSystem, a.LastAliveAt, IsOffline(a.LastAliveAt, offlineThreshold),
                a.PendingInstallRequestedAt, a.PendingRebootRequestedAt))
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
        var rejectionsByHostname = await rejectionService.GetRecentByHostnameAsync(ct);
        var rejection = ResolveActiveRejection(rejectionsByHostname.GetValueOrDefault(agent.Hostname), agent.LastAliveAt);
        var offlineThreshold = TimeSpan.FromMinutes(settingsStore.AgentOffline.ThresholdMinutes);

        return new AgentDetailDto(
            agent.Hostname, agent.DnsName, agent.OperatingSystem, agent.IpAddress, agent.AgentVersion,
            agent.Approved, agent.RebootRequired, countsByAgent.GetValueOrDefault(agent.Id), agent.LastAliveAt,
            agent.ClientCertificateThumbprint, agent.ClientCertificateThumbprintSha1, agent.ClientCertificateIssuedAt, agent.ClientCertificateExpiresAt,
            agent.PendingInstallRequestedAt, agent.LastInstallOutcome, agent.LastInstallErrorDetail, agent.LastInstallCompletedAt,
            agent.PendingRebootRequestedAt, agent.LastRebootOutcome, agent.LastRebootErrorDetail, agent.LastRebootCompletedAt, agent.BootTimeUtc,
            agent.IssuingRootThumbprint, rejection?.Reason, rejection?.Timestamp, IsOffline(agent.LastAliveAt, offlineThreshold),
            agent.LastUpdateCheckAt, agent.DesiredLogLevel, agent.ActualLogLevel, agent.DesiredUpdateCheckIntervalMinutes,
            agent.ActualUpdateCheckIntervalMinutes, agent.DesiredUpdateCheckJitterSeconds, agent.ActualUpdateCheckJitterSeconds,
            agent.DesiredAliveIntervalMinutes, agent.ActualAliveIntervalMinutes);
    }

    /// <summary>
    /// Live against the CURRENT admin-configured threshold, never a
    /// periodically-updated stored flag — see <see cref="AgentOfflineOptions"/>'s
    /// own doc comment for why. A never-heartbeated agent (<paramref name="lastAliveAt"/>
    /// null) counts as offline too — it has never been seen online.
    /// </summary>
    private static bool IsOffline(DateTimeOffset? lastAliveAt, TimeSpan threshold) =>
        lastAliveAt is null || DateTimeOffset.UtcNow - lastAliveAt.Value > threshold;

    /// <summary>
    /// A rejection only still flags an agent if there's been no successful
    /// heartbeat since it happened — otherwise the agent is presenting a
    /// working certificate again right now, and the flag must clear
    /// immediately rather than linger for the rest of
    /// <see cref="ICertificateRejectionService.GetRecentByHostnameAsync"/>'s
    /// 24h lookback window. Found by a user report: fixing a certificate
    /// problem left the warning icon showing for up to 24h after the agent
    /// was already healthy again, because the lookback window alone
    /// decided whether to show it, with no way for a later success to
    /// clear it early.
    /// </summary>
    private static CertificateRejectionDto? ResolveActiveRejection(CertificateRejectionDto? rejection, DateTimeOffset? lastAliveAt) =>
        rejection is not null && lastAliveAt is not null && lastAliveAt >= rejection.Timestamp ? null : rejection;

    public async Task<int> GetTotalPendingUpdateCountAsync(CancellationToken ct = default) =>
        (await CountFilteredPendingUpdatesByAgentAsync(ct)).Values.Sum();

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
            return ReissueCertificateResult.Failed("Agent is not approved.", ApiErrorCode.AgentNotApproved);
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

    public async Task<BulkDeleteResult> DeleteManyAsync(IReadOnlyList<string> hostnames, string initiatedBy, CancellationToken ct = default)
    {
        var agents = await db.Agents.Where(a => hostnames.Contains(a.Hostname)).ToListAsync(ct);
        var deletedHostnames = agents.Select(a => a.Hostname).ToList();

        db.Agents.RemoveRange(agents);
        await db.SaveChangesAsync(ct);

        var notFound = hostnames.Where(h => !deletedHostnames.Contains(h)).ToList();
        await auditLog.LogAsync(initiatedBy, "agent.delete.bulk", string.Join(", ", deletedHostnames), ct);

        return new BulkDeleteResult(agents.Count, notFound);
    }

    public async Task<bool> TriggerRebootAsync(string hostname, string triggeredBy, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        // Delivery is the agent's own alive-heartbeat poll picking this up,
        // exactly like PendingInstallRequestedAt — see
        // AgentRegistrationService.RecordAliveAsync and
        // AgentProtocolController.Alive.
        agent.PendingRebootRequestedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync(triggeredBy, "agent.reboot.trigger", hostname, ct);
        return true;
    }

    public async Task<BulkRebootResult> TriggerRebootManyAsync(IReadOnlyList<string> hostnames, string triggeredBy, int? scheduleRunId = null, CancellationToken ct = default)
    {
        var agents = await db.Agents.Where(a => hostnames.Contains(a.Hostname)).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var agent in agents)
        {
            agent.PendingRebootRequestedAt = now;
            agent.PendingRebootScheduleRunId = scheduleRunId;
        }

        await db.SaveChangesAsync(ct);

        var triggeredHostnames = agents.Select(a => a.Hostname).ToHashSet();
        var notFound = hostnames.Where(h => !triggeredHostnames.Contains(h)).ToList();
        await auditLog.LogAsync(triggeredBy, "agent.reboot.trigger.bulk", string.Join(", ", triggeredHostnames), ct);

        return new BulkRebootResult(agents.Count, notFound);
    }

    public async Task<bool> AcknowledgeRebootAsync(string hostname, RebootOutcome outcome, string? errorDetail, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        // Additive (updatewatch2-server#25) — same reasoning as
        // UpdateService.AcknowledgeInstallAsync's identical block.
        if (agent.PendingRebootScheduleRunId is { } scheduleRunId)
        {
            var runAgent = await db.ScheduleRunAgents
                .SingleOrDefaultAsync(ra => ra.ScheduleRunId == scheduleRunId && ra.Hostname == hostname, ct);
            if (runAgent is not null)
            {
                runAgent.RebootStatus = outcome == RebootOutcome.Succeeded ? ScheduleRunActionStatus.Delivered : ScheduleRunActionStatus.Failed;
                runAgent.ErrorDetail = outcome == RebootOutcome.Failed ? errorDetail : null;
            }
        }

        agent.PendingRebootRequestedAt = null;
        agent.PendingRebootScheduleRunId = null;
        agent.LastRebootOutcome = outcome.ToString();
        // Only ever meaningful for a Failed outcome — mirrors
        // AcknowledgeInstallAsync's identical reasoning for LastInstallErrorDetail.
        agent.LastRebootErrorDetail = outcome == RebootOutcome.Failed ? errorDetail : null;
        agent.LastRebootCompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLog.LogAsync("agent", $"agent.reboot.{outcome.ToString().ToLowerInvariant()}", hostname, ct);
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

    public async Task<bool> UpdateSettingsAsync(
        string hostname, string initiatedBy, string desiredLogLevel, int desiredUpdateCheckIntervalMinutes,
        int desiredUpdateCheckJitterSeconds, int desiredAliveIntervalMinutes, CancellationToken ct = default)
    {
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.Hostname == hostname, ct);
        if (agent is null)
        {
            return false;
        }

        agent.DesiredLogLevel = desiredLogLevel;
        agent.DesiredUpdateCheckIntervalMinutes = desiredUpdateCheckIntervalMinutes;
        agent.DesiredUpdateCheckJitterSeconds = desiredUpdateCheckJitterSeconds;
        agent.DesiredAliveIntervalMinutes = desiredAliveIntervalMinutes;
        // Set unconditionally, even if these values happen to already match
        // what's currently Desired/Actual — see Agent.PendingSettingsPush's
        // own doc comment for why this must be set on every save, not just
        // when something actually changed: a heartbeat already in flight
        // right now must not be allowed to adopt its own (possibly stale)
        // actual value back over what was just saved here.
        agent.PendingSettingsPush = true;
        await db.SaveChangesAsync(ct);

        await auditLog.LogAsync(
            initiatedBy, "agent.settings.update",
            $"{hostname}: LogLevel={desiredLogLevel}, UpdateCheckIntervalMinutes={desiredUpdateCheckIntervalMinutes}, " +
            $"UpdateCheckJitterSeconds={desiredUpdateCheckJitterSeconds}, AliveIntervalMinutes={desiredAliveIntervalMinutes}",
            ct);

        return true;
    }

    public async Task<BulkUpdateAgentSettingsResult> UpdateSettingsManyAsync(
        IReadOnlyList<string> hostnames, string initiatedBy, string? desiredLogLevel, int? desiredUpdateCheckIntervalMinutes,
        int? desiredUpdateCheckJitterSeconds, int? desiredAliveIntervalMinutes, CancellationToken ct = default)
    {
        var agents = await db.Agents.Where(a => hostnames.Contains(a.Hostname)).ToListAsync(ct);
        foreach (var agent in agents)
        {
            // Only the fields the admin actually opted into are touched —
            // see BulkUpdateAgentSettingsRequest's own doc comment for why
            // this is deliberately different from the single-agent, always-
            // full-replace overload above.
            if (desiredLogLevel is not null)
            {
                agent.DesiredLogLevel = desiredLogLevel;
            }

            if (desiredUpdateCheckIntervalMinutes is not null)
            {
                agent.DesiredUpdateCheckIntervalMinutes = desiredUpdateCheckIntervalMinutes;
            }

            if (desiredUpdateCheckJitterSeconds is not null)
            {
                agent.DesiredUpdateCheckJitterSeconds = desiredUpdateCheckJitterSeconds;
            }

            if (desiredAliveIntervalMinutes is not null)
            {
                agent.DesiredAliveIntervalMinutes = desiredAliveIntervalMinutes;
            }

            // Same "set unconditionally" reasoning as the single-agent
            // overload — even a partial push still needs to block an
            // in-flight heartbeat from adopting a stale actual value back
            // over the field(s) that were just pushed.
            agent.PendingSettingsPush = true;
        }

        await db.SaveChangesAsync(ct);

        var updatedHostnames = agents.Select(a => a.Hostname).ToHashSet();
        var notFound = hostnames.Where(h => !updatedHostnames.Contains(h)).ToList();
        await auditLog.LogAsync(
            initiatedBy, "agent.settings.update.bulk",
            $"{string.Join(", ", updatedHostnames)}: LogLevel={desiredLogLevel ?? "(unchanged)"}, " +
            $"UpdateCheckIntervalMinutes={desiredUpdateCheckIntervalMinutes?.ToString() ?? "(unchanged)"}, " +
            $"UpdateCheckJitterSeconds={desiredUpdateCheckJitterSeconds?.ToString() ?? "(unchanged)"}, " +
            $"AliveIntervalMinutes={desiredAliveIntervalMinutes?.ToString() ?? "(unchanged)"}",
            ct);

        return new BulkUpdateAgentSettingsResult(agents.Count, notFound);
    }
}
