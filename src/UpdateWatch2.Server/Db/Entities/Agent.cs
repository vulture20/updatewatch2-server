namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// A managed endpoint (Windows today, Linux planned). Identified uniquely by
/// <see cref="Hostname"/> — see CLAUDE.md "Agents are identified by hostname".
/// </summary>
public class Agent
{
    public int Id { get; set; }

    public required string Hostname { get; set; }

    public string? DnsName { get; set; }

    public string? OperatingSystem { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Version string reported by the agent itself (independent of protocol/server versions).</summary>
    public string? AgentVersion { get; set; }

    /// <summary>
    /// SHA-256 thumbprint of the client certificate issued to this agent after
    /// approval. Also doubles as the one-shot-delivery marker for
    /// <see cref="RegistrationTokenHash"/>'s flow: once set, the certificate has
    /// already been handed to the agent and will never be re-issued/re-sent —
    /// see AgentRegistrationService.
    /// </summary>
    public string? ClientCertificateThumbprint { get; set; }

    /// <summary>
    /// SHA-1 thumbprint of the same certificate as <see cref="ClientCertificateThumbprint"/>
    /// — display-only, alongside it in the admin UI, for an admin comparing
    /// against a local tool that shows SHA-1 by default (Windows Certificate
    /// Manager, PowerShell's <c>.Thumbprint</c>, <c>certutil</c>, `openssl
    /// x509 -fingerprint` without `-sha256`). Never used for any lookup or
    /// comparison — <see cref="Certificates.CertificateValidator"/> and every
    /// other internal check still use SHA-256 exclusively.
    /// </summary>
    public string? ClientCertificateThumbprintSha1 { get; set; }

    /// <summary>When the client certificate identified by <see cref="ClientCertificateThumbprint"/> was issued.</summary>
    public DateTimeOffset? ClientCertificateIssuedAt { get; set; }

    /// <summary>When the client certificate identified by <see cref="ClientCertificateThumbprint"/> expires.</summary>
    public DateTimeOffset? ClientCertificateExpiresAt { get; set; }

    /// <summary>
    /// SHA-256 thumbprint of the CA root that signed <see cref="ClientCertificateThumbprint"/>
    /// — captured at issuance/renewal time (see <see cref="Certificates.IssuedCertificate.IssuingRootThumbprintSha256"/>).
    /// Null for a certificate issued before this field existed (an unknown,
    /// not-verifiable value, deliberately never backfilled by guessing) as
    /// well as for an agent with no certificate at all. Lets
    /// <c>AgentRegistrationService.RecordAliveAsync</c> tell whether an
    /// agent's leaf still chains to the CA's CURRENT root — CA root
    /// rotation (updatewatch2-server#6) never reissues an already-onboarded
    /// agent's own leaf on its own, so without this there would be no way
    /// to know which agents are still relying on a since-superseded root.
    /// </summary>
    public string? IssuingRootThumbprint { get; set; }

    /// <summary>
    /// SHA-256 hash of the opaque registration token handed to this agent on
    /// first contact (never the raw token — same secret-hygiene convention as
    /// password/AD-bind-password storage elsewhere in this codebase). Used to
    /// prevent a different host from hijacking an in-flight, not-yet-approved
    /// registration for the same hostname. Not needed once a certificate has
    /// been delivered, so it's cleared at that point.
    /// </summary>
    public string? RegistrationTokenHash { get; set; }

    /// <summary>
    /// False until an admin manually confirms this agent (individually or via bulk approval).
    /// No client certificate is issued, and no authenticated traffic is accepted, before approval.
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>Set from the agent's most recent update-check report; independent of update installation.</summary>
    public bool RebootRequired { get; set; }

    /// <summary>
    /// Raw count of the agent's most recently reported updates, set by
    /// <see cref="Updates.UpdateService.ReportUpdatesAsync"/> — NOT what the
    /// admin UI displays. The displayed pending-update count is computed
    /// live by <see cref="Agents.AgentService"/>, excluding anything an
    /// active <see cref="UpdateFilter"/> matches, so it stays correct
    /// immediately after a filter is added/edited/deleted rather than only
    /// after this agent's next report.
    /// </summary>
    public int PendingUpdateCount { get; set; }

    /// <summary>
    /// Set when an admin triggers a remote install (updatewatch2-server#10);
    /// cleared once the agent acknowledges having acted on it (see
    /// <see cref="Updates.IUpdateService.AcknowledgeInstallAsync"/>). Delivery
    /// is poll-based — surfaced to the agent as part of its regular alive
    /// heartbeat response, not pushed.
    /// </summary>
    public DateTimeOffset? PendingInstallRequestedAt { get; set; }

    /// <summary>
    /// Which <see cref="ScheduleRun"/> set <see cref="PendingInstallRequestedAt"/>,
    /// if any — null for a manually-triggered install (which stays pending
    /// indefinitely, unchanged existing behavior). Only when this is set
    /// does <see cref="Schedules.ScheduleService"/>'s deadline-expiry sweep
    /// ever clear a still-pending install out from under an offline agent.
    /// Cleared alongside <see cref="PendingInstallRequestedAt"/> on ack or
    /// expiry.
    /// </summary>
    public int? PendingInstallScheduleRunId { get; set; }

    /// <summary>
    /// JSON-serialized array of the specific <see cref="UpdateItem.PackageId"/>
    /// values an admin selected when triggering this pending install — an
    /// admin's way to install only some pending updates while sparing
    /// others (<see cref="Updates.IUpdateService.TriggerInstallAsync"/>).
    /// Null means "install everything currently pending", the original
    /// behavior this replaces the sole option of. Not exposed on any
    /// admin-facing DTO — purely internal plumbing between
    /// <c>TriggerInstallAsync</c> (which writes it from the admin-selected
    /// <see cref="UpdateItem"/> ids) and
    /// <c>AgentRegistrationService.RecordAliveAsync</c> (which hands it
    /// back to the agent, whose own next search re-resolves these PackageIds
    /// against whatever it currently finds — this server never tells an
    /// agent to install something it can't independently re-verify is
    /// still actually pending). Cleared alongside <see cref="PendingInstallRequestedAt"/>
    /// once the agent acknowledges.
    /// </summary>
    public string? PendingInstallUpdateIds { get; set; }

    /// <summary>
    /// The <see cref="Updates.InstallOutcome"/> name (e.g. "Succeeded") from
    /// the agent's most recent install acknowledgement — a plain string, not
    /// the enum type itself, matching this codebase's existing convention
    /// for wire/DB-facing simple enums (see e.g. AdminSettings's
    /// SmtpEncryption column).
    /// </summary>
    public string? LastInstallOutcome { get; set; }

    /// <summary>
    /// Human-readable reason for the most recent install acknowledgement,
    /// only ever meaningful (and only ever set) alongside a
    /// <see cref="LastInstallOutcome"/> of "Failed" — the agent's own
    /// OS-level tool output (apt-get/dnf stderr, a Windows Update result
    /// code) or a caught exception's message, capped agent-side before it
    /// ever reaches this column. Added (DB schema 0.15.5) after a real
    /// production install failure took raising the agent's log level and
    /// live-tailing journalctl to even see — before this, an admin had no
    /// way to learn WHY an install failed from the admin UI at all. Reset
    /// to null on a Succeeded ack so a stale error never lingers next to a
    /// since-successful install.
    /// </summary>
    public string? LastInstallErrorDetail { get; set; }

    public DateTimeOffset? LastInstallCompletedAt { get; set; }

    /// <summary>
    /// Set when an admin triggers a remote machine reboot; cleared once
    /// the agent acknowledges having acted on it (see
    /// <see cref="Agents.IAgentService.AcknowledgeRebootAsync"/>). Delivery
    /// is poll-based, exactly like <see cref="PendingInstallRequestedAt"/> —
    /// surfaced to the agent as part of its regular alive heartbeat
    /// response, not pushed. Distinct from <see cref="RebootRequired"/>:
    /// that field is the agent's own self-reported "an installed OS update
    /// needs a reboot to take effect" signal (CLAUDE.md's "update
    /// installation never triggers a reboot itself — the admin decides
    /// when to actually trigger a reboot" rule); this field is that
    /// decision actually being made and delivered.
    /// </summary>
    public DateTimeOffset? PendingRebootRequestedAt { get; set; }

    /// <summary>Which <see cref="ScheduleRun"/> set <see cref="PendingRebootRequestedAt"/>, if any — same reasoning as <see cref="PendingInstallScheduleRunId"/>.</summary>
    public int? PendingRebootScheduleRunId { get; set; }

    /// <summary>
    /// Set (instead of <see cref="PendingRebootRequestedAt"/> directly) when
    /// a <see cref="ScheduleRun"/> requests install-then-reboot-if-required:
    /// the reboot need can only be known once the install actually
    /// completes, so this marks the agent as "being watched" rather than
    /// firing the reboot trigger immediately. The moment this agent's next
    /// heartbeat reports <see cref="RebootRequired"/> as true,
    /// <see cref="Agents.AgentRegistrationService.RecordAliveAsync"/> turns
    /// this into a real <see cref="PendingRebootRequestedAt"/>/<see cref="PendingRebootScheduleRunId"/>
    /// and clears this field; if the run's deadline passes first, the
    /// watch is abandoned (recorded as <c>Skipped</c> — no reboot was ever
    /// confirmed necessary) without ever sending a trigger.
    /// </summary>
    public int? PendingConditionalRebootScheduleRunId { get; set; }

    /// <summary>
    /// The <see cref="Agents.RebootOutcome"/> name (e.g. "Succeeded") from
    /// the agent's most recent reboot acknowledgement — a plain string,
    /// not the enum type itself, matching <see cref="LastInstallOutcome"/>'s
    /// own convention. "Succeeded" only ever means the platform's reboot
    /// command was scheduled successfully, not that the machine has
    /// actually come back up yet — see <see cref="BootTimeUtc"/> for that.
    /// </summary>
    public string? LastRebootOutcome { get; set; }

    /// <summary>
    /// Human-readable reason for the most recent reboot acknowledgement,
    /// only ever meaningful (and only ever set) alongside a
    /// <see cref="LastRebootOutcome"/> of "Failed" — mirrors
    /// <see cref="LastInstallErrorDetail"/>'s own reasoning. Reset to null
    /// on a Succeeded ack so a stale error never lingers next to a
    /// since-successful reboot.
    /// </summary>
    public string? LastRebootErrorDetail { get; set; }

    public DateTimeOffset? LastRebootCompletedAt { get; set; }

    /// <summary>
    /// When the agent's own machine last booted, self-reported on every
    /// alive heartbeat (like <see cref="AgentVersion"/>/<see cref="IpAddress"/>
    /// etc. — updatewatch2-agent#6's refresh channel) from
    /// <c>Environment.TickCount64</c>, which .NET implements portably on
    /// both Windows and Linux. Lets an admin actually confirm a
    /// remote-triggered reboot took effect (this value jumping forward to
    /// a recent timestamp) rather than just trusting <see cref="LastRebootOutcome"/>,
    /// which only ever reflects whether the reboot command was scheduled
    /// successfully, not whether the machine actually came back up.
    /// </summary>
    public DateTimeOffset? BootTimeUtc { get; set; }

    public DateTimeOffset? LastAliveAt { get; set; }

    /// <summary>
    /// When this agent last actually reported the result of an update
    /// check (<see cref="Updates.IUpdateService.ReportUpdatesAsync"/>) —
    /// set on every successful report, not read back from the reported
    /// items themselves (an agent with genuinely zero pending updates
    /// still reports, with an empty list, so this must be its own
    /// timestamp rather than derived from <see cref="UpdateItem.DetectedAt"/>,
    /// which would go stale the moment nothing new is found). Distinct
    /// from <see cref="LastAliveAt"/>: a heartbeat happens on its own,
    /// typically much shorter cadence and carries no update information at
    /// all — this only moves on the separate, jittered update-check
    /// cadence. Shown on the admin UI's Identity card, at the user's
    /// explicit request ("Bei den Client-Details sollte unter 'Identität'
    /// noch festgehalten werden, wann zuletzt nach Updates gesucht
    /// wurde.").
    /// </summary>
    public DateTimeOffset? LastUpdateCheckAt { get; set; }

    /// <summary>
    /// The current LogLevel this agent should be running with (DEBUG/INFO/
    /// WARNING/ERROR) — a single, bidirectionally-synced value, not an
    /// optional "override" (server v1.3.16, replacing that framing at the
    /// user's explicit request: "Der aktuelle Wert soll immer im Auswahl-
    /// bzw. Textfeld stehen... Änderungen sollen auf beiden Seiten möglich
    /// sein und direkt auf die Gegenseite gespiegelt werden."). Null only
    /// for an agent that has never sent a single heartbeat with this field
    /// populated (genuinely unknown, not "no override"). Editing it in the
    /// admin UI's Settings dialog marks <see cref="PendingSettingsPush"/>
    /// and pushes it down on every heartbeat until <see cref="ActualLogLevel"/>
    /// confirms it was applied; editing the agent's own local registry/config
    /// file instead flows the other way — see <see cref="ActualLogLevel"/>
    /// and <see cref="PendingSettingsPush"/>'s own doc comments for the full
    /// adoption logic in <c>AgentRegistrationService.RecordAliveAsync</c>.
    /// Distinct from <see cref="Admin.AdminSettings.LogLevel"/>, which only
    /// ever controls this server's own ASP.NET Core logging.
    /// </summary>
    public string? DesiredLogLevel { get; set; }

    /// <summary>
    /// This agent's own actual, currently-effective LogLevel, self-reported
    /// on every heartbeat. Always shown to the admin (the Settings dialog's
    /// hint text); also what <see cref="DesiredLogLevel"/> gets adopted
    /// from whenever it diverges and <see cref="PendingSettingsPush"/> is
    /// false — see that field's doc comment.
    /// </summary>
    public string? ActualLogLevel { get; set; }

    /// <summary>The current update-check interval (minutes) this agent should be running with — same bidirectional-sync semantics as <see cref="DesiredLogLevel"/>.</summary>
    public int? DesiredUpdateCheckIntervalMinutes { get; set; }

    /// <summary>This agent's own actual update-check interval, self-reported every heartbeat — same reasoning as <see cref="ActualLogLevel"/>.</summary>
    public int? ActualUpdateCheckIntervalMinutes { get; set; }

    /// <summary>The current update-check jitter (seconds) this agent should be running with — same bidirectional-sync semantics as <see cref="DesiredLogLevel"/>.</summary>
    public int? DesiredUpdateCheckJitterSeconds { get; set; }

    /// <summary>This agent's own actual update-check jitter, self-reported every heartbeat — same reasoning as <see cref="ActualLogLevel"/>.</summary>
    public int? ActualUpdateCheckJitterSeconds { get; set; }

    /// <summary>
    /// The current alive-heartbeat interval (minutes) this agent should be
    /// running with — same bidirectional-sync semantics as
    /// <see cref="DesiredLogLevel"/>, added later (server v1.3.20, at the
    /// user's explicit request — "Mache bitte auch die Client-Einstellungen
    /// für den Alive-Intervall in dem Agent-Einstellungsdialog verfügbar.")
    /// than the other three pushed settings. Live-applied on the agent side
    /// for free, the same way <c>UpdateCheckIntervalMinutes</c> already is —
    /// <c>HeartbeatWorker</c>'s own loop reads this value fresh right before
    /// its next <c>Task.Delay</c>, after <c>ApplyPushedSettings</c> has
    /// already mutated it earlier in the same tick.
    /// </summary>
    public int? DesiredAliveIntervalMinutes { get; set; }

    /// <summary>This agent's own actual alive-heartbeat interval, self-reported every heartbeat — same reasoning as <see cref="ActualLogLevel"/>.</summary>
    public int? ActualAliveIntervalMinutes { get; set; }

    /// <summary>
    /// True from the moment an admin saves a change via the Settings dialog
    /// (<see cref="Agents.AgentService.UpdateSettingsAsync"/>) or the
    /// overview list's bulk settings dialog
    /// (<see cref="Agents.AgentService.UpdateSettingsManyAsync"/>) until a
    /// later heartbeat confirms the agent actually applied it (<c>Actual*</c>
    /// matching every <c>Desired*</c> field again) — at which point
    /// <c>AgentRegistrationService.RecordAliveAsync</c> clears it back to
    /// false. This is what makes "the server always wins on a race
    /// condition" (CLAUDE.md, at the user's explicit request) actually
    /// correct rather than merely usual: while true, a heartbeat reporting
    /// a still-divergent <c>Actual*</c> is NEVER adopted back into
    /// <c>Desired*</c> — without this guard, a heartbeat already in flight
    /// the moment an admin saves (carrying the agent's OLD, pre-push actual
    /// value) would otherwise immediately stomp the admin's own just-saved
    /// change, every single time, not just in some rare true race. Once
    /// false (settled/converged), any future divergent <c>Actual*</c> IS
    /// adopted into <c>Desired*</c> — this is what lets a manual registry/
    /// config-file edit surface in the dialog the next time it's opened.
    /// One shared flag for all four settings, not one per field: the
    /// per-agent Settings dialog always saves all four together, and even
    /// the bulk settings dialog's per-field opt-in (server v1.3.20) doesn't
    /// need its own per-field flag — a field the bulk push left untouched
    /// already has <c>Desired* == Actual*</c> in steady state, so it can
    /// never be what keeps this flag from clearing; only a field that was
    /// actually just pushed can.
    /// </summary>
    public bool PendingSettingsPush { get; set; }

    /// <summary>
    /// Internal bookkeeping for <see cref="Notifications.AgentOfflineNotificationWorker"/>
    /// only — NOT what the admin UI's offline icon/filter reflects (that's
    /// always computed live from <see cref="LastAliveAt"/> against the
    /// current <see cref="Agents.AgentOfflineOptions.ThresholdMinutes"/>,
    /// the same "never trust a periodically-updated stored flag for
    /// display" precedent <see cref="Agents.AgentService"/>'s filtered
    /// pending-update count already established). Holds the last offline
    /// state the worker actually finished handling (notified, or decided
    /// not to email but still recorded) — deliberately NOT "is this agent
    /// offline right now"; a mismatch against a freshly computed live
    /// check is what "pending, not yet handled" means, and it persists
    /// unmodified across ticks until a send attempt actually succeeds or
    /// is skipped, which is what lets a failed send retry cleanly on the
    /// next tick. An earlier version of this worker instead gated on
    /// <see cref="OfflineNotifiedAt"/> being null, which made a brand-new
    /// agent that had never gone offline at all indistinguishable from one
    /// genuinely pending a "back online" notification (both have
    /// <c>OfflineCrossed = false</c>, <c>OfflineNotifiedAt = null</c>) —
    /// caught by <c>Does_not_notify_while_the_agent_is_within_the_threshold</c>.
    /// </summary>
    public bool OfflineCrossed { get; set; }

    /// <summary>
    /// Purely informational — the last time this agent was actually
    /// handled by <see cref="Notifications.AgentOfflineNotificationWorker"/>
    /// (notified, or recorded as skipped). Deliberately does not gate
    /// anything; see <see cref="OfflineCrossed"/>'s own doc comment for why.
    /// </summary>
    public DateTimeOffset? OfflineNotifiedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
