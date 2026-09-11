import { apiClient, API_BASE_URL } from './client';
import type {
  AdminSettings,
  AgentDetail,
  AgentListItem,
  AgentUpdateStatus,
  AuditLogPage,
  BulkApproveResult,
  CaRotationStatus,
  CertificateRejectionStatus,
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

/** See NotificationsController — wires up the previously-unreachable SendTestEmailAsync. */
export const notificationsApi = {
  testEmail: (toAddress: string) => apiClient.post<void>('/api/admin/notifications/test-email', { toAddress }),
};

/** CA root rotation (updatewatch2-server#6) — see CertificateAuthorityController. */
export const certificateAuthorityApi = {
  getStatus: () => apiClient.get<CaRotationStatus>('/api/admin/certificate-authority'),
  prepareRotation: () => apiClient.post<CaRotationStatus>('/api/admin/certificate-authority/prepare'),
  activateRotation: () => apiClient.post<CaRotationStatus>('/api/admin/certificate-authority/activate'),
  retirePreviousRoot: () => apiClient.post<CaRotationStatus>('/api/admin/certificate-authority/retire-previous'),
  // A raw file download, not JSON — not routed through apiClient.get<T>().
  // Consumed directly as an <a href> so the browser's normal top-level
  // navigation carries the SameSite=Lax session cookie; lets an admin
  // pre-seed a fresh agent install's CA trust and skip trust-on-first-use.
  downloadUrl: `${API_BASE_URL}/api/admin/certificate-authority/download`,
};

/** Agent auto-update status (updatewatch2-server#14) — see AgentUpdatesController. The enabled/token toggle itself is part of adminApi's settings, not this. */
export const agentUpdatesApi = {
  getStatus: () => apiClient.get<AgentUpdateStatus>('/api/admin/agent-update-status'),
  // Runs the same check AgentUpdateCheckWorker runs on its own interval,
  // right now — returns the freshly updated status (same shape as
  // getStatus) rather than a separate "outcome" type, since the refreshed
  // checkedAt/latestVersion/lastError already tell the admin what happened.
  checkNow: () => apiClient.post<AgentUpdateStatus>('/api/admin/agent-update-status/check'),
  // The offline/air-gapped escape hatch: an admin uploads the release
  // assets (any of .exe/.deb/.rpm, one of each at most) directly instead
  // of this server ever needing to reach GitHub. Also returns the freshly
  // updated status, same reasoning as checkNow above.
  upload: (version: string, files: File[]) => {
    const formData = new FormData();
    formData.append('version', version);
    files.forEach((file) => formData.append('files', file));
    return apiClient.postForm<AgentUpdateStatus>('/api/admin/agent-update-status/upload', formData);
  },
};

/** Global update filters — see UpdateFiltersController. */
export const updateFiltersApi = {
  list: () => apiClient.get<UpdateFilter[]>('/api/admin/update-filters'),
  create: (filter: UpsertUpdateFilter) => apiClient.post<UpdateFilter>('/api/admin/update-filters', filter),
  update: (id: number, filter: UpsertUpdateFilter) => apiClient.put<UpdateFilter>(`/api/admin/update-filters/${id}`, filter),
  delete: (id: number) => apiClient.delete<void>(`/api/admin/update-filters/${id}`),
};

/** Rejected agent client certificate attempts — see CertificateRejectionsController. */
export const certificateRejectionsApi = {
  getStatus: () => apiClient.get<CertificateRejectionStatus>('/api/admin/certificate-rejections'),
  // Silences the warning banner for everything recorded so far (shared
  // across every admin session, audit-logged) — returns the refreshed
  // status, same "return the freshly recomputed state" shape as
  // agentUpdatesApi.checkNow.
  acknowledge: () => apiClient.post<CertificateRejectionStatus>('/api/admin/certificate-rejections/acknowledge'),
};

/** Read-only, paginated audit log — see AuditLogController. */
export const auditLogApi = {
  getPage: (page: number, pageSize: number, search?: string) => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
    if (search) {
      params.set('search', search);
    }
    return apiClient.get<AuditLogPage>(`/api/admin/audit-log?${params.toString()}`);
  },
};
