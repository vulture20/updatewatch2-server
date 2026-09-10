import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { adminApi, agentUpdatesApi, auditLogApi, certificateAuthorityApi, updateFiltersApi, versionApi } from '../api/endpoints';
import { AdminPage } from '../pages/AdminPage';
import { SmtpWarningBanner } from './SmtpWarningBanner';

vi.mock('../api/endpoints', () => ({
  adminApi: {
    getSettings: vi.fn(),
    updateSettings: vi.fn(),
  },
  versionApi: {
    get: vi.fn(),
  },
  certificateAuthorityApi: {
    getStatus: vi.fn(),
  },
  agentUpdatesApi: {
    getStatus: vi.fn(),
  },
  updateFiltersApi: {
    list: vi.fn(),
  },
  auditLogApi: {
    getPage: vi.fn(),
  },
}));

const mockedGetSettings = vi.mocked(adminApi.getSettings);
const mockedUpdateSettings = vi.mocked(adminApi.updateSettings);

const baseSettings = {
  logLevel: 'INFO',
  bruteForceMaxAttempts: 6,
  bruteForceWindowMinutes: 5,
  bruteForceLockoutMinutes: 30,
  smtpHost: '',
  smtpPort: 587,
  smtpUsername: null,
  smtpPasswordSet: false,
  smtpEncryption: 'StartTls' as const,
  smtpFromAddress: '',
  smtpFromName: '',
  notificationRecipientAddress: null,
  smtpConfigured: false,
  notificationUpdatesPerMachineThreshold: 5,
  notificationAffectedMachinesThreshold: 10,
  adEnabled: false,
  adHost: '',
  adPort: 389,
  adEncryption: 'StartTls' as const,
  adBindDn: '',
  adBindPasswordSet: false,
  adBaseDn: '',
  adUserSearchFilter: '(&(objectClass=user)(sAMAccountName={0}))',
  adLoginGroupDn: '',
  adConfigured: false,
  agentCertificateValidityDays: 730,
  agentAutoUpdateEnabled: true,
  gitHubTokenSet: false,
  agentAutoUpdateCheckIntervalHours: 6,
  auditLogRetentionDays: 90,
  certificateExpiryWarningLeadDays: 60,
  certificateExpiryNotificationsEnabled: true,
};

// Renders both siblings the way App.tsx actually does — SmtpWarningBanner
// is not a child of AdminPage, so this is the only way to exercise the
// cross-component event that's supposed to connect them.
function renderBannerAndAdminPage() {
  return render(
    <MemoryRouter>
      <SmtpWarningBanner />
      <AdminPage />
    </MemoryRouter>,
  );
}

describe('SmtpWarningBanner', () => {
  beforeEach(() => {
    mockedGetSettings.mockReset().mockResolvedValue(baseSettings);
    mockedUpdateSettings.mockReset();
    vi.mocked(versionApi.get).mockReset().mockResolvedValue({ server: '0.29.0', protocol: '0.1.0', database: '0.3.0' });
    vi.mocked(certificateAuthorityApi.getStatus).mockReset().mockResolvedValue({
      currentThumbprint: 'AAAA',
      currentNotAfter: '2036-01-01T00:00:00Z',
      previousThumbprint: null,
      previousNotAfter: null,
      pendingThumbprint: null,
      pendingNotAfter: null,
      stillOnPreviousRootCount: 0,
      stillOnPreviousRootHostnames: [],
      unknownRootAgentCount: 0,
    });
    vi.mocked(agentUpdatesApi.getStatus).mockReset().mockResolvedValue({
      enabled: true,
      latestVersion: null,
      checkedAt: null,
      lastError: null,
    });
    vi.mocked(updateFiltersApi.list).mockReset().mockResolvedValue([]);
    vi.mocked(auditLogApi.getPage).mockReset().mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 50 });
  });

  it('disappears the moment AdminPage saves a now-configured SMTP server, with no reload', async () => {
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, smtpHost: 'smtp.example.com', smtpConfigured: true });
    const user = userEvent.setup();

    renderBannerAndAdminPage();

    expect(await screen.findByRole('alert')).toBeInTheDocument();

    await screen.findByLabelText('SMTP host');
    await user.type(screen.getByLabelText('SMTP host'), 'smtp.example.com');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  });

  it('keeps showing the warning if the save leaves SMTP still unconfigured', async () => {
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, smtpConfigured: false });
    const user = userEvent.setup();

    renderBannerAndAdminPage();

    expect(await screen.findByRole('alert')).toBeInTheDocument();

    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByRole('status');

    expect(screen.getByRole('alert')).toBeInTheDocument();
  });
});
