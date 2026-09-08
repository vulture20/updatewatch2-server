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
  lastInstallCompletedAt: string | null;
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
  previousThumbprint: string | null;
  previousNotAfter: string | null;
  pendingThumbprint: string | null;
  pendingNotAfter: string | null;
  stillOnPreviousRootCount: number;
  stillOnPreviousRootHostnames: string[];
  unknownRootAgentCount: number;
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
  smtpConfigured: boolean;
  notificationUpdatesPerMachineThreshold: number;
  notificationAffectedMachinesThreshold: number;
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
