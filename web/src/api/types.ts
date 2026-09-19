// Mirrors the server DTOs in UpdateWatch2.Server (Agents/AgentDtos.cs,
// Admin/AdminSettingsDto.cs, Auth/AuthDtos.cs,
// Api/Controllers/VersionController.cs). Kept in sync by hand for now.

export interface MeResponse {
  authenticated: boolean;
  username: string | null;
}

export interface LoginResponse {
  username: string;
}

export interface AgentListItem {
  hostname: string;
  approved: boolean;
  rebootRequired: boolean;
  pendingUpdateCount: number;
  /** Set when this agent presented an invalid/expired/unrecognized client certificate within the last 24 hours — flags the row with a warning icon. */
  lastCertificateRejectionReason: string | null;
  /** Same free-text string as AgentDetail.operatingSystem (e.g. "Windows Server 2022") — drives the per-row OS icon and the OS/OS-family filter. */
  operatingSystem: string | null;
  lastAliveAt: string | null;
  /** Live-computed from lastAliveAt against the current admin-configured offline threshold — never a stored/stale flag. Drives the offline icon and the online/offline filter. */
  isOffline: boolean;
  /** Non-null while an admin-triggered install is pending/in progress — drives the "Updates" activity badge for an approved agent. */
  pendingInstallRequestedAt: string | null;
  /** Non-null while an admin-triggered reboot is pending/in progress — drives the "Neustart"/reboot activity badge. */
  pendingRebootRequestedAt: string | null;
  /** Same self-reported string as AgentDetail.agentVersion — shown in the outdated-icon tooltip alongside isOutdated. */
  agentVersion: string | null;
  /** Live-computed: true when agentVersion is older than the newest agent release the server currently knows about. Drives the outdated-agent icon. */
  isOutdated: boolean;
}

export interface AgentDetail {
  hostname: string;
  dnsName: string | null;
  operatingSystem: string | null;
  ipAddress: string | null;
  agentVersion: string | null;
  approved: boolean;
  rebootRequired: boolean;
  pendingUpdateCount: number;
  lastAliveAt: string | null;
  clientCertificateThumbprint: string | null;
  clientCertificateThumbprintSha1: string | null;
  clientCertificateIssuedAt: string | null;
  clientCertificateExpiresAt: string | null;
  /** Set while a remote install (triggerInstall) has been requested but not yet acknowledged by the agent (updatewatch2-server#10). */
  pendingInstallRequestedAt: string | null;
  lastInstallOutcome: 'Succeeded' | 'Failed' | null;
  /** Only ever non-null alongside lastInstallOutcome === 'Failed' — the agent's own OS-level tool output (apt-get/dnf stderr, a Windows Update result code) or a caught exception's message. */
  lastInstallErrorDetail: string | null;
  lastInstallCompletedAt: string | null;
  /** Set while a remote reboot (triggerReboot) has been requested but not yet acknowledged by the agent. Reboots the agent's own machine — never just the agent's service process, never the OS-update install pipeline. */
  pendingRebootRequestedAt: string | null;
  /** Only ever reflects whether the platform's reboot command was scheduled successfully — whether the machine actually came back up is instead visible via bootTimeUtc jumping forward on a later heartbeat. */
  lastRebootOutcome: 'Succeeded' | 'Failed' | null;
  /** Only ever non-null alongside lastRebootOutcome === 'Failed' — mirrors lastInstallErrorDetail. */
  lastRebootErrorDetail: string | null;
  lastRebootCompletedAt: string | null;
  /** When the agent's own machine last booted, self-reported every heartbeat — jumping forward to a recent timestamp is how an admin confirms a triggered reboot actually took effect. */
  bootTimeUtc: string | null;
  /**
   * SHA-256 thumbprint of the internal CA root that signed this agent's
   * current client certificate — compare against the Administration →
   * Certificates tab's current/previous root thumbprints to see whether
   * this agent has renewed past a CA root rotation yet. Null for a
   * certificate issued before this was tracked, or no certificate at all.
   */
  issuingRootThumbprint: string | null;
  /** Same as AgentListItem.lastCertificateRejectionReason — shown on the detail page alongside when it happened. */
  lastCertificateRejectionReason: string | null;
  lastCertificateRejectionAt: string | null;
  /** Same live computation as AgentListItem.isOffline. */
  isOffline: boolean;
  /** When this agent last reported the result of an update check — distinct from lastAliveAt, which moves on the much shorter, separate heartbeat cadence. */
  lastUpdateCheckAt: string | null;
  /** Admin-set LogLevel override, pushed to and enforced by the agent every heartbeat — null means no override, the agent's own local registry/config file value decides. Edited in the Settings dialog. */
  desiredLogLevel: string | null;
  /** This agent's own actual, currently-effective LogLevel, self-reported every heartbeat — shown for visibility even with no override set. */
  actualLogLevel: string | null;
  /** Admin-set override for the agent's update-check interval (minutes) — same null-means-no-override semantics as desiredLogLevel. */
  desiredUpdateCheckIntervalMinutes: number | null;
  /** This agent's own actual update-check interval, self-reported every heartbeat. */
  actualUpdateCheckIntervalMinutes: number | null;
  /** Admin-set override for the agent's update-check jitter (seconds) — same null-means-no-override semantics as desiredLogLevel. */
  desiredUpdateCheckJitterSeconds: number | null;
  /** This agent's own actual update-check jitter, self-reported every heartbeat. */
  actualUpdateCheckJitterSeconds: number | null;
  /** Admin-set override for the agent's alive-heartbeat interval (minutes) — same null-means-no-override semantics as desiredLogLevel. */
  desiredAliveIntervalMinutes: number | null;
  /** This agent's own actual alive-heartbeat interval, self-reported every heartbeat. */
  actualAliveIntervalMinutes: number | null;
}

/**
 * Body of PUT /api/agents/{hostname}/settings — a full replace, see
 * agentsApi.updateSettings. All four fields are required: this sets the
 * agent's current value for each setting (bidirectionally synced, not an
 * optional override), matching AgentDetail's own desired* fields. Not to be
 * confused with BulkAgentSettingsUpdate, the overview list's bulk-push
 * counterpart, whose fields are each independently optional instead.
 */
export interface UpdateAgentSettings {
  desiredLogLevel: string;
  desiredUpdateCheckIntervalMinutes: number;
  desiredUpdateCheckJitterSeconds: number;
  desiredAliveIntervalMinutes: number;
}

/**
 * Body of POST /api/agents/settings — the overview list's bulk-push
 * counterpart to UpdateAgentSettings, at the user's explicit request ("Es
 * fehlt außerdem die Möglichkeit Agent-Einstellungen bulk zu pushen.").
 * Every settings field is independently optional: omitted/undefined means
 * "leave this setting untouched on every selected agent" — confirmed as the
 * intended design via an explicit clarifying question before implementing,
 * so an admin can push just one setting (e.g. LogLevel=DEBUG on several
 * agents at once) without being forced to also overwrite the others'
 * individually-tuned values.
 */
export interface BulkAgentSettingsUpdate {
  hostnames: string[];
  desiredLogLevel?: string;
  desiredUpdateCheckIntervalMinutes?: number;
  desiredUpdateCheckJitterSeconds?: number;
  desiredAliveIntervalMinutes?: number;
}

/** Response of an admin-initiated certificate re-issuance (updatewatch2-server#8). */
export interface ReissueCertificateResult {
  registrationToken: string;
}

export interface UpdateItem {
  id: number;
  title: string;
  packageId: string | null;
  description: string | null;
  detectedAt: string;
  installed: boolean;
}

export interface BulkApproveResult {
  approvedCount: number;
  notFoundHostnames: string[];
}

export interface BulkDeleteResult {
  deletedCount: number;
  notFoundHostnames: string[];
}

export interface BulkInstallResult {
  triggeredCount: number;
  notFoundHostnames: string[];
}

export interface BulkRebootResult {
  triggeredCount: number;
  notFoundHostnames: string[];
}

export interface BulkAgentSettingsResult {
  updatedCount: number;
  notFoundHostnames: string[];
}

export interface VersionInfo {
  server: string;
  protocol: string;
  database: string;
}

/**
 * CA root rotation state (updatewatch2-server#6) — see CertificateAuthorityController.
 * stillOnPreviousRootCount/unknownRootAgentCount are the updatewatch2-server#6
 * follow-up: how many/which approved agents' own client leaf would stop
 * authenticating if the previous root were retired right now (0/empty
 * whenever previousThumbprint is null), so "Retire Previous Root" shows a
 * real number instead of only generic warning text.
 * stillOnPreviousRootHostnames may be capped short of stillOnPreviousRootCount
 * for a large fleet — the count is always the true total.
 * unknownRootAgentCount is a separate "can't verify" bucket (a certificate
 * issued before this tracking existed) — never folded into the confirmed count.
 */
export interface CaRotationStatus {
  currentThumbprint: string;
  currentNotAfter: string;
  currentNotBefore: string;
  currentSubject: string;
  currentIssuer: string;
  currentSerialNumber: string;
  previousThumbprint: string | null;
  previousNotAfter: string | null;
  previousNotBefore: string | null;
  previousSubject: string | null;
  previousIssuer: string | null;
  previousSerialNumber: string | null;
  pendingThumbprint: string | null;
  pendingNotAfter: string | null;
  pendingNotBefore: string | null;
  pendingSubject: string | null;
  pendingIssuer: string | null;
  pendingSerialNumber: string | null;
  stillOnPreviousRootCount: number;
  stillOnPreviousRootHostnames: string[];
  unknownRootAgentCount: number;
  /** The server's own agent-facing mTLS leaf (Kestrel's port-8796 listener) — not part of CA rotation itself, just surfaced alongside it since both are "relevant certificates" (Info tab). */
  serverLeafThumbprint: string;
  serverLeafSubject: string;
  serverLeafIssuer: string;
  serverLeafSerialNumber: string;
  serverLeafNotBefore: string;
  serverLeafNotAfter: string;
}

export type SmtpEncryption = 'None' | 'SslTls' | 'StartTls';
export type AdEncryption = 'None' | 'StartTls' | 'Ldaps';

export interface AdminSettings {
  logLevel: string;
  bruteForceMaxAttempts: number;
  bruteForceWindowMinutes: number;
  bruteForceLockoutMinutes: number;
  smtpHost: string;
  smtpPort: number;
  smtpUsername: string | null;
  smtpPasswordSet: boolean;
  smtpEncryption: SmtpEncryption;
  smtpFromAddress: string;
  smtpFromName: string;
  /** Where automated alert emails go (certificate-expiry warnings today) — null/empty means nothing is configured to receive them. */
  notificationRecipientAddress: string | null;
  /** The externally-reachable base URL of this instance, added as a link/button to every notification email — null/empty means no link is added. */
  instanceUrl: string | null;
  smtpConfigured: boolean;
  notificationUpdatesPerMachineThreshold: number;
  /** Independent on/off checkbox for the updates-per-machine half of the threshold notification — see UpdateThresholdNotificationWorker. Default true. */
  notificationUpdatesPerMachineEnabled: boolean;
  notificationAffectedMachinesThreshold: number;
  /** Independent on/off checkbox for the affected-machines half of the threshold notification — see UpdateThresholdNotificationWorker. Default true. */
  notificationAffectedMachinesEnabled: boolean;
  adEnabled: boolean;
  adHost: string;
  adPort: number;
  adEncryption: AdEncryption;
  adBindDn: string;
  adBindPasswordSet: boolean;
  adBaseDn: string;
  adUserSearchFilter: string;
  adLoginGroupDn: string;
  adConfigured: boolean;
  agentCertificateValidityDays: number;
  agentAutoUpdateEnabled: boolean;
  gitHubTokenSet: boolean;
  agentAutoUpdateCheckIntervalHours: number;
  /** 0 = unlimited/never discard. Fixed UI steps: 30/60/90/180/365, or unlimited — see AdminController's server-side validation. */
  auditLogRetentionDays: number;
  /** Days before NotAfter the server treats the CA root/server leaf as "approaching expiry" — see CertificateExpiryWorker. Default 60. */
  certificateExpiryWarningLeadDays: number;
  /** On/off switch for the CA-root/server-leaf expiry emails specifically — doesn't affect the server leaf's own unconditional self-renewal. Default true. */
  certificateExpiryNotificationsEnabled: boolean;
  /** Minutes since an agent's last heartbeat before it's considered offline (General tab). Default 15 — see AgentOfflineOptions. */
  agentOfflineThresholdMinutes: number;
  /** On/off switch for the "agent went offline" email — see AgentOfflineNotificationWorker. Default true. */
  agentOfflineNotificationEnabled: boolean;
  /** Independent on/off switch for the "agent back online" email. Default true. */
  agentOnlineRecoveryNotificationEnabled: boolean;
  /** Whether a Windows agent should proactively download pending Windows Updates ahead of an install trigger — surfaced to agents via the alive heartbeat response. Default true. */
  preDownloadWindowsUpdatesEnabled: boolean;
  /** Same idea as preDownloadWindowsUpdatesEnabled, for a Linux agent's apt/dnf package manager — a genuinely independent toggle, not derived from the Windows one. Default true. */
  preDownloadLinuxUpdatesEnabled: boolean;
  /** IANA time zone ID (e.g. "Europe/Berlin") used to interpret a Recurring/Cron schedule's bare time-of-day/cron expression. Default "UTC". */
  timeZoneId: string;
  /** How many rows the agent overview and schedules lists show per page. 0 = unlimited (everything on one page). Fixed UI steps: 10/25/50/100/200, or unlimited. Default 50. Originally also governed the audit log's pagination — see auditLogItemsPerPage. */
  itemsPerPage: number;
  /** Same idea as itemsPerPage, but specifically for the Audit Log — a genuinely independent setting, not an override. Default 50. */
  auditLogItemsPerPage: number;
}

/**
 * Backs SmtpWarningBanner's reachability half (updatewatch2-server#12) —
 * a cached result from SmtpHealthCheckWorker's own periodic check, never a
 * live probe performed by this request itself. checkedAt is null until
 * that worker has run at least once (briefly, right after a fresh
 * startup).
 */
export interface SmtpHealthStatus {
  healthy: boolean;
  checkedAt: string | null;
}

/**
 * smtpPassword/adBindPassword/gitHubToken: undefined/omitted leaves the
 * stored value unchanged; an empty string clears it. There is no way to
 * read any of them back out (AdminSettings only has the *Set booleans),
 * so the form must default these to undefined and only set one when the
 * admin actually types a new value.
 */
/** Read-only companion to AdminSettings.agentAutoUpdateEnabled — see AgentUpdatesController. */
export interface AgentUpdateStatus {
  enabled: boolean;
  latestVersion: string | null;
  checkedAt: string | null;
  lastError: string | null;
  /** True iff latestVersion's assets were manually uploaded (agentUpdatesApi.upload) rather than downloaded from GitHub. */
  manuallyUploaded: boolean;
}

export type UpdateAdminSettings = Omit<
  AdminSettings,
  'smtpPasswordSet' | 'smtpConfigured' | 'adBindPasswordSet' | 'adConfigured' | 'gitHubTokenSet'
> & {
  smtpPassword?: string;
  adBindPassword?: string;
  gitHubToken?: string;
};

/**
 * A named regex filter (see UpdateFiltersController) — any update whose
 * title matches any filter's pattern is excluded from the pending-updates
 * display, live as filters are added/edited/deleted, not just for future
 * agent reports.
 */
export interface UpdateFilter {
  id: number;
  name: string;
  pattern: string;
  createdAt: string;
}

export interface UpsertUpdateFilter {
  name: string;
  pattern: string;
}

/**
 * One rejected agent client certificate attempt — see
 * CertificateRejectionsController. `reason` is one of
 * CertificateRejectionReason's values ("Expired", "NotYetValid",
 * "NotTrusted", "UnknownAgent", "AgentNotApproved"), kept as a raw string
 * here rather than a union type since new reasons may be added server-side
 * without a matching web release.
 */
export interface CertificateRejection {
  timestamp: string;
  reason: string;
  details: string | null;
}

/** Backs the admin UI's rejected-certificate warning banner — recentCount is the true total even if recent itself is capped. */
export interface CertificateRejectionStatus {
  recentCount: number;
  recent: CertificateRejection[];
}

/** One audit log row — see AuditLogController. */
export interface AuditLogEntry {
  id: number;
  timestamp: string;
  actor: string;
  action: string;
  details: string | null;
}

/** One page of the audit log, newest first — totalCount is the true total across every page, not just this one. */
export interface AuditLogPage {
  entries: AuditLogEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/**
 * A named, admin-defined maintenance window (see SchedulesController) —
 * a fixed list of agents, a firing pattern, and an action (install
 * updates and/or reboot). Purely server-side orchestration on top of the
 * existing install/reboot delivery mechanism — the agent itself is
 * unaware schedules exist at all.
 */
export type ScheduleType = 'Once' | 'Recurring' | 'Cron';
export type SchedulePattern = 'Weekly' | 'IntervalDays';
export type ScheduleStatus = 'Active' | 'Paused' | 'Completed';
export type WeekdayName = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday';

export interface Schedule {
  id: number;
  name: string;
  enabled: boolean;
  status: ScheduleStatus;
  scheduleType: ScheduleType;
  pattern: SchedulePattern | null;
  onceAt: string | null;
  weeklyDays: WeekdayName[] | null;
  /** "HH:MM:SS" (a serialized .NET TimeSpan), in the server's own local time zone — see ScheduleRecurrenceCalculator's doc comment for why there's no per-schedule time zone. */
  timeOfDay: string;
  intervalDays: number | null;
  /** "YYYY-MM-DD" (a serialized .NET DateOnly). */
  intervalStartDate: string | null;
  /** Standard 5-field cron expression — only set when scheduleType is 'Cron'. */
  cronExpression: string | null;
  actionInstall: boolean;
  actionReboot: boolean;
  /** Only meaningful when actionReboot is true — see UpsertSchedule's own doc comment on the combined-with-install case. */
  rebootOnlyIfRequired: boolean;
  deadlineHours: number;
  /** Whether a failed install/reboot or a missed action for this schedule sends a notification email. Default true. */
  notifyOnFailure: boolean;
  nextRunAt: string | null;
  lastRunAt: string | null;
  hostnames: string[];
}

/**
 * Same editable fields as Schedule, used for both create and update. For
 * an install+reboot schedule with rebootOnlyIfRequired, the reboot need
 * can only be known once the install actually completes — the server
 * watches for it on a later heartbeat rather than deciding at fire time,
 * see the server's own AgentRegistrationService.RecordAliveAsync.
 */
export interface UpsertSchedule {
  name: string;
  enabled: boolean;
  scheduleType: ScheduleType;
  pattern: SchedulePattern | null;
  onceAt: string | null;
  weeklyDays: WeekdayName[] | null;
  timeOfDay: string;
  intervalDays: number | null;
  intervalStartDate: string | null;
  cronExpression: string | null;
  actionInstall: boolean;
  actionReboot: boolean;
  rebootOnlyIfRequired: boolean;
  deadlineHours: number;
  notifyOnFailure: boolean;
  hostnames: string[];
}

export type ScheduleRunActionStatus = 'NotApplicable' | 'Skipped' | 'AwaitingInstallResult' | 'Pending' | 'Delivered' | 'Missed' | 'Failed';

export interface ScheduleRunAgent {
  hostname: string;
  installStatus: ScheduleRunActionStatus;
  rebootStatus: ScheduleRunActionStatus;
  errorDetail: string | null;
}

/** One actual firing of a Schedule — see ScheduleRun/ScheduleRunAgent server-side. */
export interface ScheduleRun {
  id: number;
  firedAt: string;
  deadlineAt: string;
  actionInstall: boolean;
  actionReboot: boolean;
  rebootOnlyIfRequired: boolean;
  agents: ScheduleRunAgent[];
}
