namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// The single, persisted row <see cref="Notifications.UpdateThresholdNotificationWorker"/>
/// uses to avoid re-sending the same "threshold crossed" email on every
/// check — the same one-row-table convention as <see cref="AdminSettings"/>/
/// <see cref="AgentUpdateState"/>/<see cref="CertificateNotificationState"/>.
///
/// Unlike <see cref="CertificateNotificationState"/>'s thumbprint-keyed
/// tracking (a certificate naturally identifies "which generation" was
/// already warned about), the two threshold conditions here
/// (CLAUDE.md: "an OR of two independent conditions") have no equivalent
/// identity to key on — a condition is either currently crossed or it
/// isn't. So each is tracked edge-triggered instead: a notification fires
/// once when a condition transitions from not-crossed to crossed (the
/// <c>*Crossed</c> flag flips false→true), never again on a later tick
/// while it stays crossed, and the flag resets to false — allowing a fresh
/// notification later — only once the underlying count genuinely drops
/// back below its threshold. The two conditions are independent of each
/// other, matching their independent admin-configurable on/off checkboxes
/// (<see cref="Notifications.NotificationThresholdOptions.UpdatesPerMachineEnabled"/>/
/// <see cref="Notifications.NotificationThresholdOptions.AffectedMachinesEnabled"/>).
/// </summary>
public class UpdateThresholdNotificationState
{
    public int Id { get; set; }

    /// <summary>Whether the updates-per-machine threshold was crossed as of the last check.</summary>
    public bool UpdatesPerMachineCrossed { get; set; }

    /// <summary>
    /// Null while the current crossing above is still waiting on a
    /// successful notification attempt (or there's nowhere to notify, in
    /// which case this is set immediately since there's nothing to retry) —
    /// the same "audit-log/email atomically, only once actually handled"
    /// pattern <see cref="CertificateNotificationState.LastServerLeafRenewalNotifiedAt"/> uses.
    /// </summary>
    public DateTimeOffset? UpdatesPerMachineNotifiedAt { get; set; }

    /// <summary>Whether the affected-machines threshold was crossed as of the last check.</summary>
    public bool AffectedMachinesCrossed { get; set; }

    public DateTimeOffset? AffectedMachinesNotifiedAt { get; set; }
}
