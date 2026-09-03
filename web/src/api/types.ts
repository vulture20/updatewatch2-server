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

export interface AdminSettings {
  logLevel: string;
  bruteForceMaxAttempts: number;
  bruteForceWindowMinutes: number;
  bruteForceLockoutMinutes: number;
  smtpConfigured: boolean;
  notificationUpdatesPerMachineThreshold: number;
  notificationAffectedMachinesThreshold: number;
}
