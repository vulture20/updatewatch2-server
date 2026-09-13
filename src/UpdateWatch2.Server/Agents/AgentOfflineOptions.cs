namespace UpdateWatch2.Server.Agents;

/// <summary>
/// Admin-configurable offline detection (Settings → General), at the
/// user's explicit request: "Warnschwelle festlegen, ab wann ein Client
/// als offline gilt und diesen dann markieren und eventuell per Mail
/// informieren." An agent whose <see cref="Db.Entities.Agent.LastAliveAt"/>
/// is older than <see cref="ThresholdMinutes"/> (or has never heartbeated
/// at all) is considered offline — computed live on every
/// <see cref="AgentService.GetAllAsync"/>/<see cref="AgentService.GetByHostnameAsync"/>
/// call, the same "never trust a periodically-updated stored flag for
/// what the UI displays" precedent <see cref="AgentService"/>'s own
/// filtered pending-update count already established, so a threshold
/// change takes effect immediately rather than waiting on the next
/// notification-worker tick.
///
/// <see cref="OfflineNotificationEnabled"/>/<see cref="OnlineRecoveryNotificationEnabled"/>
/// (Settings → Notifications, both default true, independently
/// switchable per the user's explicit request) gate only the EMAIL half
/// of this feature — the icon/filter always reflect the live threshold
/// regardless of either toggle. See <see cref="Notifications.AgentOfflineNotificationWorker"/>
/// for what actually evaluates and sends these.
/// </summary>
public class AgentOfflineOptions
{
    public const string SectionName = "AgentOffline";

    public int ThresholdMinutes { get; set; } = 15;

    public bool OfflineNotificationEnabled { get; set; } = true;

    public bool OnlineRecoveryNotificationEnabled { get; set; } = true;
}
