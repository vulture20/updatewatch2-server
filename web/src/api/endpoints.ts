import { apiClient, API_BASE_URL } from './client';
import type {
  AdminSettings,
  AgentDetail,
  AgentListItem,
  AgentUpdateStatus,
  AuditLogPage,
  BulkApproveResult,
  BulkDeleteResult,
  BulkInstallResult,
  BulkRebootResult,
  CaRotationStatus,
  CertificateRejectionStatus,
  LoginResponse,
  MeResponse,
  ReissueCertificateResult,
  SmtpHealthStatus,
  UpdateAdminSettings,
  UpdateAgentSettings,
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
  // updateItemIds: an admin's way to install only some pending updates
  // while sparing others (checkboxes in AgentDetailPage) — omitted
  // installs everything currently pending, the original behavior.
  triggerInstall: (hostname: string, updateItemIds?: number[]) =>
    apiClient.post<void>(`/api/agents/${encodeURIComponent(hostname)}/install`, updateItemIds ? { updateItemIds } : undefined),
  // Always installs everything pending for each named agent — there is no
  // bulk equivalent of triggerInstall's per-agent updateItemIds selection
  // (see BulkInstallRequest's own doc comment, server-side, for why).
  installMany: (hostnames: string[]) =>
    apiClient.post<BulkInstallResult>('/api/agents/install', { hostnames }),
  // Reboots the agent's own machine — not just its service process, and
  // not the OS-update install pipeline. Fire-and-forget, delivered on the
  // agent's next alive heartbeat, mirroring triggerInstall exactly.
  triggerReboot: (hostname: string) =>
    apiClient.post<void>(`/api/agents/${encodeURIComponent(hostname)}/reboot`),
  rebootMany: (hostnames: string[]) =>
    apiClient.post<BulkRebootResult>('/api/agents/reboot', { hostnames }),
  reissueCertificate: (hostname: string) =>
    apiClient.post<ReissueCertificateResult>(`/api/agents/${encodeURIComponent(hostname)}/reissue-certificate`),
  // Full replace, not a partial merge — a field left null clears any
  // existing override for that setting, matching PUT /api/admin/settings'
  // own "replaces the whole object" convention.
  updateSettings: (hostname: string, settings: UpdateAgentSettings) =>
    apiClient.put<void>(`/api/agents/${encodeURIComponent(hostname)}/settings`, settings),
  delete: (hostname: string) => apiClient.delete<void>(`/api/agents/${encodeURIComponent(hostname)}`),
  deleteMany: (hostnames: string[]) =>
    apiClient.post<BulkDeleteResult>('/api/agents/delete', { hostnames }),
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
  // Cached, not a live probe — see SmtpHealthStatus's own doc comment (updatewatch2-server#12).
  getSmtpHealth: () => apiClient.get<SmtpHealthStatus>('/api/admin/notifications/smtp-health'),
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
  // updated status, same reasoning as checkNow above. No version is passed
  // — the server extracts it from the uploaded filenames itself
  // (AgentUpdateVersionExtractor) rather than trusting a separately typed
  // value that could mismatch what's actually in the files.
  upload: (files: File[]) => {
    const formData = new FormData();
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
