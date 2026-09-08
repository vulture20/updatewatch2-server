import { apiClient } from './client';
import type {
  AdminSettings,
  AgentDetail,
  AgentListItem,
  AgentUpdateStatus,
  BulkApproveResult,
  CaRotationStatus,
  LoginResponse,
  MeResponse,
  ReissueCertificateResult,
  UpdateAdminSettings,
  UpdateFilter,
  UpdateItem,
  UpsertUpdateFilter,
  VersionInfo,
} from './types';

export const authApi = {
  // skipUnauthorizedHandler: a failed login attempt is not a "session
  // expired" event, and /me's whole point is to answer "am I logged in?" —
  // neither should trigger the global unauthorized handler.
  me: () => apiClient.get<MeResponse>('/api/auth/me', { skipUnauthorizedHandler: true }),
  login: (username: string, password: string) =>
    apiClient.post<LoginResponse>('/api/auth/login', { username, password }, { skipUnauthorizedHandler: true }),
  logout: () => apiClient.post<void>('/api/auth/logout'),
  changePassword: (currentPassword: string, newPassword: string) =>
    apiClient.put<void>('/api/auth/password', { currentPassword, newPassword }),
};

export const agentsApi = {
  list: () => apiClient.get<AgentListItem[]>('/api/agents'),
  get: (hostname: string) => apiClient.get<AgentDetail>(`/api/agents/${encodeURIComponent(hostname)}`),
  approve: (hostname: string) => apiClient.post<void>(`/api/agents/${encodeURIComponent(hostname)}/approve`),
  approveMany: (hostnames: string[]) =>
    apiClient.post<BulkApproveResult>('/api/agents/approve', { hostnames }),
  updates: (hostname: string) => apiClient.get<UpdateItem[]>(`/api/agents/${encodeURIComponent(hostname)}/updates`),
  triggerInstall: (hostname: string) => apiClient.post<void>(`/api/agents/${encodeURIComponent(hostname)}/install`),
  reissueCertificate: (hostname: string) =>
    apiClient.post<ReissueCertificateResult>(`/api/agents/${encodeURIComponent(hostname)}/reissue-certificate`),
  delete: (hostname: string) => apiClient.delete<void>(`/api/agents/${encodeURIComponent(hostname)}`),
};

export const versionApi = {
  get: () => apiClient.get<VersionInfo>('/api/version'),
};

export const adminApi = {
  getSettings: () => apiClient.get<AdminSettings>('/api/admin/settings'),
  updateSettings: (settings: UpdateAdminSettings) =>
    apiClient.put<AdminSettings>('/api/admin/settings', settings),
};

/** CA root rotation (updatewatch2-server#6) — see CertificateAuthorityController. */
export const certificateAuthorityApi = {
  getStatus: () => apiClient.get<CaRotationStatus>('/api/admin/certificate-authority'),
  prepareRotation: () => apiClient.post<CaRotationStatus>('/api/admin/certificate-authority/prepare'),
  activateRotation: () => apiClient.post<CaRotationStatus>('/api/admin/certificate-authority/activate'),
  retirePreviousRoot: () => apiClient.post<CaRotationStatus>('/api/admin/certificate-authority/retire-previous'),
};

/** Agent auto-update status (updatewatch2-server#14) — see AgentUpdatesController. The enabled/token toggle itself is part of adminApi's settings, not this. */
export const agentUpdatesApi = {
  getStatus: () => apiClient.get<AgentUpdateStatus>('/api/admin/agent-update-status'),
  // Runs the same check AgentUpdateCheckWorker runs on its own interval,
  // right now — returns the freshly updated status (same shape as
  // getStatus) rather than a separate "outcome" type, since the refreshed
  // checkedAt/latestVersion/lastError already tell the admin what happened.
  checkNow: () => apiClient.post<AgentUpdateStatus>('/api/admin/agent-update-status/check'),
};

/** Global update filters — see UpdateFiltersController. */
export const updateFiltersApi = {
  list: () => apiClient.get<UpdateFilter[]>('/api/admin/update-filters'),
  create: (filter: UpsertUpdateFilter) => apiClient.post<UpdateFilter>('/api/admin/update-filters', filter),
  update: (id: number, filter: UpsertUpdateFilter) => apiClient.put<UpdateFilter>(`/api/admin/update-filters/${id}`, filter),
  delete: (id: number) => apiClient.delete<void>(`/api/admin/update-filters/${id}`),
};
