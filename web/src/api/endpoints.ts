import { apiClient } from './client';
import type {
  AdminSettings,
  AgentDetail,
  AgentListItem,
  BulkApproveResult,
  LoginResponse,
  MeResponse,
  ReissueCertificateResult,
  UpdateAdminSettings,
  UpdateItem,
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
};

export const versionApi = {
  get: () => apiClient.get<VersionInfo>('/api/version'),
};

export const adminApi = {
  getSettings: () => apiClient.get<AdminSettings>('/api/admin/settings'),
  updateSettings: (settings: UpdateAdminSettings) =>
    apiClient.put<AdminSettings>('/api/admin/settings', settings),
};
