import { apiClient } from './client';
import type {
  AdminSettings,
  AgentDetail,
  AgentListItem,
  BulkApproveResult,
  UpdateItem,
  VersionInfo,
} from './types';

export const agentsApi = {
  list: () => apiClient.get<AgentListItem[]>('/api/agents'),
  get: (hostname: string) => apiClient.get<AgentDetail>(`/api/agents/${encodeURIComponent(hostname)}`),
  approve: (hostname: string) => apiClient.post<void>(`/api/agents/${encodeURIComponent(hostname)}/approve`),
  approveMany: (hostnames: string[]) =>
    apiClient.post<BulkApproveResult>('/api/agents/approve', { hostnames }),
  updates: (hostname: string) => apiClient.get<UpdateItem[]>(`/api/agents/${encodeURIComponent(hostname)}/updates`),
  triggerInstall: (hostname: string) => apiClient.post<void>(`/api/agents/${encodeURIComponent(hostname)}/install`),
};

export const versionApi = {
  get: () => apiClient.get<VersionInfo>('/api/version'),
};

export const adminApi = {
  getSettings: () => apiClient.get<AdminSettings>('/api/admin/settings'),
};
