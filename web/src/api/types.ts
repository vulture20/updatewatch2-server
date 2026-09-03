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

export type SmtpEncryption = 'None' | 'SslTls' | 'StartTls';

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
}

/**
 * smtpPassword: undefined/omitted leaves the stored password unchanged;
 * an empty string clears it. There is no way to read the current password
 * back out (AdminSettings only has smtpPasswordSet), so the form must
 * default this to undefined and only set it when the admin actually types
 * a new one.
 */
export type UpdateAdminSettings = Omit<AdminSettings, 'smtpPasswordSet' | 'smtpConfigured'> & {
  smtpPassword?: string;
};
