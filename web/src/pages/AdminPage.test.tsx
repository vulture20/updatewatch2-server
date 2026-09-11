import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { adminApi, agentUpdatesApi, auditLogApi, certificateAuthorityApi, notificationsApi, updateFiltersApi, versionApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { AdminPage } from './AdminPage';

vi.mock('../api/endpoints', () => ({
  adminApi: {
    getSettings: vi.fn(),
    updateSettings: vi.fn(),
  },
  notificationsApi: {
    testEmail: vi.fn(),
  },
  versionApi: {
    get: vi.fn(),
  },
  certificateAuthorityApi: {
    getStatus: vi.fn(),
    prepareRotation: vi.fn(),
    activateRotation: vi.fn(),
    retirePreviousRoot: vi.fn(),
    downloadUrl: 'http://localhost/api/admin/certificate-authority/download',
  },
  agentUpdatesApi: {
    getStatus: vi.fn(),
    checkNow: vi.fn(),
  },
  updateFiltersApi: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
  },
  auditLogApi: {
    getPage: vi.fn(),
  },
}));

const mockedGetSettings = vi.mocked(adminApi.getSettings);
const mockedUpdateSettings = vi.mocked(adminApi.updateSettings);
const mockedTestEmail = vi.mocked(notificationsApi.testEmail);
const mockedGetVersion = vi.mocked(versionApi.get);
const mockedGetCaStatus = vi.mocked(certificateAuthorityApi.getStatus);
const mockedPrepareRotation = vi.mocked(certificateAuthorityApi.prepareRotation);
const mockedActivateRotation = vi.mocked(certificateAuthorityApi.activateRotation);
const mockedRetirePreviousRoot = vi.mocked(certificateAuthorityApi.retirePreviousRoot);
const mockedGetAgentUpdateStatus = vi.mocked(agentUpdatesApi.getStatus);
const mockedCheckNow = vi.mocked(agentUpdatesApi.checkNow);
const mockedListUpdateFilters = vi.mocked(updateFiltersApi.list);
const mockedCreateUpdateFilter = vi.mocked(updateFiltersApi.create);
const mockedUpdateUpdateFilter = vi.mocked(updateFiltersApi.update);
const mockedDeleteUpdateFilter = vi.mocked(updateFiltersApi.delete);
const mockedGetAuditLogPage = vi.mocked(auditLogApi.getPage);

const baseCaStatus = {
  currentThumbprint: 'AAAA',
  currentNotAfter: '2036-01-01T00:00:00Z',
  currentNotBefore: '2026-01-01T00:00:00Z',
  currentSubject: 'CN=UpdateWatch2 Internal CA',
  currentIssuer: 'CN=UpdateWatch2 Internal CA',
  currentSerialNumber: '01',
  previousThumbprint: null,
  previousNotAfter: null,
  previousNotBefore: null,
  previousSubject: null,
  previousIssuer: null,
  previousSerialNumber: null,
  pendingThumbprint: null,
  pendingNotAfter: null,
  pendingNotBefore: null,
  pendingSubject: null,
  pendingIssuer: null,
  pendingSerialNumber: null,
  stillOnPreviousRootCount: 0,
  stillOnPreviousRootHostnames: [] as string[],
  unknownRootAgentCount: 0,
  serverLeafThumbprint: 'CCCC',
  serverLeafSubject: 'CN=updatewatch2.example.com',
  serverLeafIssuer: 'CN=UpdateWatch2 Internal CA',
  serverLeafSerialNumber: '02',
  serverLeafNotBefore: '2026-01-01T00:00:00Z',
  serverLeafNotAfter: '2028-01-01T00:00:00Z',
};

const baseSettings = {
  logLevel: 'INFO',
  bruteForceMaxAttempts: 6,
  bruteForceWindowMinutes: 5,
  bruteForceLockoutMinutes: 30,
  smtpHost: 'smtp.example.com',
  smtpPort: 587,
  smtpUsername: 'notifier',
  smtpPasswordSet: true,
  smtpEncryption: 'StartTls' as const,
  smtpFromAddress: 'updatewatch2@example.com',
  smtpFromName: 'UpdateWatch2',
  notificationRecipientAddress: null,
  smtpConfigured: true,
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

describe('AdminPage', () => {
  beforeEach(() => {
    mockedGetSettings.mockReset().mockResolvedValue(baseSettings);
    mockedUpdateSettings.mockReset();
    mockedGetVersion.mockReset().mockResolvedValue({ server: '0.3.0', protocol: '0.1.0', database: '0.3.0' });
    mockedGetCaStatus.mockReset().mockResolvedValue(baseCaStatus);
    mockedPrepareRotation.mockReset();
    mockedActivateRotation.mockReset();
    mockedRetirePreviousRoot.mockReset();
    mockedGetAgentUpdateStatus.mockReset().mockResolvedValue({
      enabled: true,
      latestVersion: null,
      checkedAt: null,
      lastError: null,
    });
    mockedCheckNow.mockReset();
    mockedListUpdateFilters.mockReset().mockResolvedValue([]);
    mockedGetAuditLogPage.mockReset().mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 50 });
    mockedCreateUpdateFilter.mockReset();
    mockedUpdateUpdateFilter.mockReset();
    mockedDeleteUpdateFilter.mockReset();
    mockedTestEmail.mockReset();
  });

  it('renders the loaded settings into the form fields', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );

    expect(await screen.findByLabelText('SMTP host')).toHaveValue('smtp.example.com');
    expect(screen.getByLabelText('Max attempts')).toHaveValue(6);
    expect(screen.getByLabelText('Log level')).toHaveValue('INFO');
    // The current password is never sent by the server — the field starts empty.
    expect(screen.getByLabelText('SMTP password')).toHaveValue('');
    expect(screen.getByLabelText('Notification recipient')).toHaveValue('');
    await user.click(screen.getByRole('tab', { name: 'Certificates' }));
    expect(screen.getByLabelText('Certificate expiry lead time (days)')).toHaveValue(60);
  });

  it('submits the edited form and shows a saved confirmation', async () => {
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, bruteForceMaxAttempts: 9 });
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');

    const maxAttempts = screen.getByLabelText('Max attempts');
    await user.clear(maxAttempts);
    await user.type(maxAttempts, '9');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Settings saved.');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(
      expect.objectContaining({ bruteForceMaxAttempts: 9, smtpPassword: undefined }),
    );
  });

  it('sends a typed password as smtpPassword, and omits it when left blank', async () => {
    mockedUpdateSettings.mockResolvedValue(baseSettings);
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Notifications' }));
    await user.type(screen.getByLabelText('SMTP password'), 'new-secret');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ smtpPassword: 'new-secret' }));
  });

  it('switches to the Active Directory tab and submits its settings', async () => {
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, adEnabled: true });
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Active Directory' }));

    await user.click(screen.getByLabelText('Enable Active Directory login'));
    await user.type(screen.getByLabelText('LDAP host'), 'ldap.example.com');
    await user.type(screen.getByLabelText('Search base DN'), 'dc=example,dc=com');
    await user.type(screen.getByLabelText('Login group DN'), 'cn=admins,dc=example,dc=com');
    await user.type(screen.getByLabelText('Service account password'), 'ad-secret');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(
      expect.objectContaining({
        adEnabled: true,
        adHost: 'ldap.example.com',
        adBaseDn: 'dc=example,dc=com',
        adLoginGroupDn: 'cn=admins,dc=example,dc=com',
        adBindPassword: 'ad-secret',
      }),
    );
  });

  it('switches to the Certificates tab and submits an edited validity period', async () => {
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, agentCertificateValidityDays: 90 });
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Certificates' }));

    const validity = screen.getByLabelText('Agent certificate validity (days)');
    await user.clear(validity);
    await user.type(validity, '90');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ agentCertificateValidityDays: 90 }));
  });

  it('switches to the Audit Log tab and shows entries from the API', async () => {
    mockedGetAuditLogPage.mockResolvedValue({
      entries: [{ id: 1, timestamp: '2026-01-01T00:00:00Z', actor: 'admin', action: 'agent.approve', details: 'host-1' }],
      totalCount: 1,
      page: 1,
      pageSize: 50,
    });
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Audit Log' }));

    expect(await screen.findByText('agent.approve')).toBeInTheDocument();
  });

  it('shows the latest known agent version and toggles auto-update off', async () => {
    mockedGetAgentUpdateStatus.mockResolvedValue({
      enabled: true,
      latestVersion: '0.11.0',
      checkedAt: '2026-01-01T00:00:00Z',
      lastError: null,
    });
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, agentAutoUpdateEnabled: false });
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');

    expect(await screen.findByText('0.11.0')).toBeInTheDocument();

    await user.click(screen.getByLabelText('Check for and distribute new agent releases'));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ agentAutoUpdateEnabled: false }));
  });

  it('runs a manual agent-update check and shows the refreshed status', async () => {
    mockedGetAgentUpdateStatus.mockResolvedValue({
      enabled: true,
      latestVersion: '0.11.0',
      checkedAt: '2026-01-01T00:00:00Z',
      lastError: null,
    });
    mockedCheckNow.mockResolvedValue({
      enabled: true,
      latestVersion: '0.12.2',
      checkedAt: '2026-02-01T00:00:00Z',
      lastError: null,
    });
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    expect(await screen.findByText('0.11.0')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Check now' }));

    expect(mockedCheckNow).toHaveBeenCalledTimes(1);
    expect(await screen.findByText('0.12.2')).toBeInTheDocument();
  });

  it('shows an error message when a manual check fails', async () => {
    mockedCheckNow.mockRejectedValue(new ApiError(500, 'GitHub is unreachable.'));
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('button', { name: 'Check now' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('GitHub is unreachable.');
  });

  it('disables Check now while the feature itself is off', async () => {
    mockedGetAgentUpdateStatus.mockResolvedValue({ enabled: false, latestVersion: null, checkedAt: null, lastError: null });

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );

    expect(await screen.findByRole('button', { name: 'Check now' })).toBeDisabled();
  });

  it('submits an edited agent auto-update check interval', async () => {
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, agentAutoUpdateCheckIntervalHours: 24 });
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');

    const interval = screen.getByLabelText('Check interval (hours)');
    await user.clear(interval);
    await user.type(interval, '24');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ agentAutoUpdateCheckIntervalHours: 24 }));
  });

  it('submits an edited audit log retention', async () => {
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, auditLogRetentionDays: 30 });
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');

    await user.selectOptions(screen.getByLabelText('Audit log retention'), '30');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ auditLogRetentionDays: 30 }));
  });

  it('offers unlimited as a retention option', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    // The dropdown's own tab isn't active by default — its <option>s are
    // only in the accessibility tree (and so findable by role) once its
    // hidden={} div is actually shown.
    await user.click(screen.getByRole('tab', { name: 'Audit Log' }));

    expect(screen.getByRole('option', { name: 'Unlimited (never discard)' })).toHaveValue('0');
  });

  it('submits an edited notification recipient and certificate expiry lead time', async () => {
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, notificationRecipientAddress: 'alerts@example.com', certificateExpiryWarningLeadDays: 30 });
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');

    await user.type(screen.getByLabelText('Notification recipient'), 'alerts@example.com');
    await user.click(screen.getByRole('tab', { name: 'Certificates' }));
    const leadDays = screen.getByLabelText('Certificate expiry lead time (days)');
    await user.clear(leadDays);
    await user.type(leadDays, '30');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(
      expect.objectContaining({ notificationRecipientAddress: 'alerts@example.com', certificateExpiryWarningLeadDays: 30 }),
    );
  });

  it('turns certificate expiry notifications off and submits the change', async () => {
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, certificateExpiryNotificationsEnabled: false });
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Notifications' }));

    const toggle = screen.getByLabelText('Enabled', { selector: 'input[type="checkbox"]' });
    expect(toggle).toBeChecked();
    await user.click(toggle);
    expect(toggle).not.toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ certificateExpiryNotificationsEnabled: false }));
  });

  it('sends a test email and shows a confirmation', async () => {
    mockedTestEmail.mockResolvedValue(undefined);
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Notifications' }));

    await user.type(screen.getByLabelText('Send a test email to'), 'me@example.com');
    await user.click(screen.getByRole('button', { name: 'Send test email' }));

    expect(await screen.findByText('Sent.')).toBeInTheDocument();
    expect(mockedTestEmail).toHaveBeenCalledWith('me@example.com');
  });

  it('shows an error message when sending a test email fails', async () => {
    mockedTestEmail.mockRejectedValue(new ApiError(400, 'SMTP is not configured.'));
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Notifications' }));

    await user.type(screen.getByLabelText('Send a test email to'), 'me@example.com');
    await user.click(screen.getByRole('button', { name: 'Send test email' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('SMTP is not configured.');
  });

  it('sends a typed GitHub token as gitHubToken, and omits it when left blank', async () => {
    mockedUpdateSettings.mockResolvedValue(baseSettings);
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ gitHubToken: undefined }));

    await user.type(screen.getByLabelText('GitHub token (optional)'), 'ghp_newtoken');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(mockedUpdateSettings).toHaveBeenLastCalledWith(expect.objectContaining({ gitHubToken: 'ghp_newtoken' }));
  });

  it('keeps General-tab edits when saving after switching to another tab', async () => {
    // Fields on hidden tabs must stay mounted (not unmounted), or edits
    // made before switching tabs would be lost on submit.
    mockedUpdateSettings.mockResolvedValue(baseSettings);
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');

    const maxAttempts = screen.getByLabelText('Max attempts');
    await user.clear(maxAttempts);
    await user.type(maxAttempts, '9');

    await user.click(screen.getByRole('tab', { name: 'Notifications' }));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ bruteForceMaxAttempts: 9 }));
  });

  it('shows the server validation error message on a failed save', async () => {
    mockedUpdateSettings.mockRejectedValue(new ApiError(400, 'SmtpPort must be between 1 and 65535.'));
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('SmtpPort must be between 1 and 65535.');
  });
});

describe('AdminPage CA root rotation (updatewatch2-server#6)', () => {
  beforeEach(() => {
    mockedGetSettings.mockReset().mockResolvedValue(baseSettings);
    mockedUpdateSettings.mockReset();
    mockedGetVersion.mockReset().mockResolvedValue({ server: '0.3.0', protocol: '0.1.0', database: '0.3.0' });
    mockedGetCaStatus.mockReset().mockResolvedValue(baseCaStatus);
    mockedPrepareRotation.mockReset();
    mockedActivateRotation.mockReset();
    mockedRetirePreviousRoot.mockReset();
    mockedGetAgentUpdateStatus.mockReset().mockResolvedValue({
      enabled: true,
      latestVersion: null,
      checkedAt: null,
      lastError: null,
    });
    mockedCheckNow.mockReset();
    mockedListUpdateFilters.mockReset().mockResolvedValue([]);
    mockedGetAuditLogPage.mockReset().mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 50 });
    mockedCreateUpdateFilter.mockReset();
    mockedUpdateUpdateFilter.mockReset();
    mockedDeleteUpdateFilter.mockReset();
    mockedTestEmail.mockReset();
  });

  const openCertificatesTab = async (user: ReturnType<typeof userEvent.setup>) => {
    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Certificates' }));
  };

  // AdminPage formats these with `i18n.language` (resolved to 'en' here via
  // the language detector's navigator fallback, see setupTests.ts) rather
  // than a hardcoded literal — computing the expected string the same way
  // keeps this assertion correct regardless of which ICU/date formatting
  // the machine running the test happens to have (this hardcoded a
  // European D.M.YYYY-style literal once, which passed wherever it was
  // authored but failed on GitHub Actions' runner, which formats 'en'
  // dates as M/D/YYYY — never hardcode a locale-formatted date again).
  const expiresLabel = (iso: string) => `expires ${new Date(iso).toLocaleDateString('en')}`;

  it('shows the current root and disables Activate/Retire when there is nothing pending or previous', async () => {
    const user = userEvent.setup();
    await openCertificatesTab(user);

    expect(await screen.findByText(`AAAA (${expiresLabel('2036-01-01T00:00:00Z')})`)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Activate rotation' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Retire previous root' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Prepare rotation' })).toBeEnabled();
    expect(screen.getByRole('link', { name: 'Download CA root certificate' })).toHaveAttribute(
      'href',
      certificateAuthorityApi.downloadUrl,
    );
  });

  it('prepares a rotation and reflects the newly pending root', async () => {
    mockedPrepareRotation.mockResolvedValue({ ...baseCaStatus, pendingThumbprint: 'BBBB', pendingNotAfter: '2036-06-01T00:00:00Z' });
    const user = userEvent.setup();
    await openCertificatesTab(user);

    await user.click(await screen.findByRole('button', { name: 'Prepare rotation' }));

    expect(mockedPrepareRotation).toHaveBeenCalledTimes(1);
    expect(await screen.findByText(`BBBB (${expiresLabel('2036-06-01T00:00:00Z')})`)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Activate rotation' })).toBeEnabled();
  });

  it('asks for confirmation before activating and does nothing if declined', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    mockedGetCaStatus.mockResolvedValue({ ...baseCaStatus, pendingThumbprint: 'BBBB', pendingNotAfter: '2036-06-01T00:00:00Z' });
    const user = userEvent.setup();
    await openCertificatesTab(user);

    await user.click(await screen.findByRole('button', { name: 'Activate rotation' }));

    expect(mockedActivateRotation).not.toHaveBeenCalled();
  });

  it('activates a prepared rotation after confirmation', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    mockedGetCaStatus.mockResolvedValue({ ...baseCaStatus, pendingThumbprint: 'BBBB', pendingNotAfter: '2036-06-01T00:00:00Z' });
    mockedActivateRotation.mockResolvedValue({
      ...baseCaStatus,
      currentThumbprint: 'BBBB',
      currentNotAfter: '2036-06-01T00:00:00Z',
      previousThumbprint: 'AAAA',
      previousNotAfter: '2036-01-01T00:00:00Z',
      pendingThumbprint: null,
      pendingNotAfter: null,
    });
    const user = userEvent.setup();
    await openCertificatesTab(user);

    await user.click(await screen.findByRole('button', { name: 'Activate rotation' }));

    expect(mockedActivateRotation).toHaveBeenCalledTimes(1);
    expect(await screen.findByText(`BBBB (${expiresLabel('2036-06-01T00:00:00Z')})`)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retire previous root' })).toBeEnabled();
  });

  it('retires the previous root after confirmation', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    mockedGetCaStatus.mockResolvedValue({ ...baseCaStatus, previousThumbprint: 'AAAA', previousNotAfter: '2036-01-01T00:00:00Z' });
    mockedRetirePreviousRoot.mockResolvedValue(baseCaStatus);
    const user = userEvent.setup();
    await openCertificatesTab(user);

    await user.click(await screen.findByRole('button', { name: 'Retire previous root' }));

    expect(mockedRetirePreviousRoot).toHaveBeenCalledTimes(1);
    expect(await screen.findByRole('button', { name: 'Retire previous root' })).toBeDisabled();
  });

  it('shows how many agents are still on the previous root, and asks for confirmation with that live count', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    mockedGetCaStatus.mockResolvedValue({
      ...baseCaStatus,
      previousThumbprint: 'AAAA',
      previousNotAfter: '2036-01-01T00:00:00Z',
      stillOnPreviousRootCount: 2,
      stillOnPreviousRootHostnames: ['host-a', 'host-b'],
      unknownRootAgentCount: 1,
    });
    mockedRetirePreviousRoot.mockResolvedValue(baseCaStatus);
    const user = userEvent.setup();
    await openCertificatesTab(user);

    expect(await screen.findByText(/Agents still on the previous root: 2/)).toBeInTheDocument();
    expect(screen.getByText(/host-a, host-b/)).toBeInTheDocument();
    expect(screen.getByText(/1 agent\(s\) have a certificate issued before this could be tracked/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Retire previous root' }));

    expect(confirmSpy).toHaveBeenCalledWith(expect.stringContaining('2 agent(s)'));
  });

  it('shows an error message when a rotation action fails', async () => {
    mockedPrepareRotation.mockRejectedValue(new ApiError(500, 'Something went wrong.'));
    const user = userEvent.setup();
    await openCertificatesTab(user);

    await user.click(await screen.findByRole('button', { name: 'Prepare rotation' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Something went wrong.');
  });

  it('shows the server certificate and current CA root on the Info tab, and the previous/pending roots only when present', async () => {
    mockedGetCaStatus.mockResolvedValue({
      ...baseCaStatus,
      previousThumbprint: 'PREV1',
      previousNotAfter: '2027-01-01T00:00:00Z',
      previousNotBefore: '2025-01-01T00:00:00Z',
      previousSubject: 'CN=UpdateWatch2 Internal CA (previous)',
      previousIssuer: 'CN=UpdateWatch2 Internal CA (previous)',
      previousSerialNumber: '00',
      pendingThumbprint: 'PEND1',
      pendingNotAfter: '2037-01-01T00:00:00Z',
      pendingNotBefore: '2027-01-01T00:00:00Z',
      pendingSubject: 'CN=UpdateWatch2 Internal CA (pending)',
      pendingIssuer: 'CN=UpdateWatch2 Internal CA (pending)',
      pendingSerialNumber: '03',
    });
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Info' }));

    expect(await screen.findByText('Server certificate (agent connections)')).toBeInTheDocument();
    expect(screen.getByText('CN=updatewatch2.example.com')).toBeInTheDocument();
    expect(screen.getByText('CCCC')).toBeInTheDocument();

    expect(screen.getByText('CA root (current)')).toBeInTheDocument();
    expect(screen.getByText('AAAA')).toBeInTheDocument();

    expect(screen.getByText('CA root (previous, still trusted)')).toBeInTheDocument();
    expect(screen.getByText('PREV1')).toBeInTheDocument();

    expect(screen.getByText('CA root (prepared, not yet active)')).toBeInTheDocument();
    expect(screen.getByText('PEND1')).toBeInTheDocument();
  });

  it('hides the previous/pending CA root cards on the Info tab when there is no rotation in progress', async () => {
    mockedGetCaStatus.mockResolvedValue(baseCaStatus);
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Info' }));

    await screen.findByText('CA root (current)');
    expect(screen.queryByText('CA root (previous, still trusted)')).not.toBeInTheDocument();
    expect(screen.queryByText('CA root (prepared, not yet active)')).not.toBeInTheDocument();
  });
});

describe('AdminPage update filters', () => {
  beforeEach(() => {
    mockedGetSettings.mockReset().mockResolvedValue(baseSettings);
    mockedUpdateSettings.mockReset();
    mockedGetVersion.mockReset().mockResolvedValue({ server: '0.3.0', protocol: '0.1.0', database: '0.3.0' });
    mockedGetCaStatus.mockReset().mockResolvedValue(baseCaStatus);
    mockedGetAgentUpdateStatus.mockReset().mockResolvedValue({
      enabled: true,
      latestVersion: null,
      checkedAt: null,
      lastError: null,
    });
    mockedListUpdateFilters.mockReset().mockResolvedValue([]);
    mockedGetAuditLogPage.mockReset().mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 50 });
    mockedCreateUpdateFilter.mockReset();
    mockedUpdateUpdateFilter.mockReset();
    mockedDeleteUpdateFilter.mockReset();
    mockedTestEmail.mockReset();
  });

  const openUpdateFiltersTab = async (user: ReturnType<typeof userEvent.setup>) => {
    render(
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>,
    );
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Update filters' }));
  };

  it('lists the filters returned by the API', async () => {
    mockedListUpdateFilters.mockResolvedValue([
      { id: 1, name: 'Defender', pattern: 'Security Intelligence-Update', createdAt: '2026-01-01T00:00:00Z' },
    ]);
    const user = userEvent.setup();

    await openUpdateFiltersTab(user);

    expect(await screen.findByText('Defender')).toBeInTheDocument();
    expect(screen.getByText('Security Intelligence-Update')).toBeInTheDocument();
  });

  it('shows a placeholder when there are no filters', async () => {
    const user = userEvent.setup();

    await openUpdateFiltersTab(user);

    expect(await screen.findByText('No filters defined.')).toBeInTheDocument();
  });

  it('adds a new filter and reloads the list', async () => {
    mockedCreateUpdateFilter.mockResolvedValue({ id: 2, name: 'Edge', pattern: 'Microsoft Edge', createdAt: '2026-01-01T00:00:00Z' });
    const user = userEvent.setup();
    await openUpdateFiltersTab(user);
    mockedListUpdateFilters.mockResolvedValue([
      { id: 2, name: 'Edge', pattern: 'Microsoft Edge', createdAt: '2026-01-01T00:00:00Z' },
    ]);

    await user.type(screen.getByLabelText('Name'), 'Edge');
    await user.type(screen.getByLabelText('Pattern (regular expression)'), 'Microsoft Edge');
    await user.click(screen.getByRole('button', { name: 'Add filter' }));

    expect(mockedCreateUpdateFilter).toHaveBeenCalledWith({ name: 'Edge', pattern: 'Microsoft Edge' });
    expect(await screen.findByText('Edge')).toBeInTheDocument();
  });

  it('disables Add filter until both name and pattern are filled in', async () => {
    const user = userEvent.setup();
    await openUpdateFiltersTab(user);

    expect(screen.getByRole('button', { name: 'Add filter' })).toBeDisabled();

    await user.type(screen.getByLabelText('Name'), 'Edge');
    expect(screen.getByRole('button', { name: 'Add filter' })).toBeDisabled();

    await user.type(screen.getByLabelText('Pattern (regular expression)'), 'Microsoft Edge');
    expect(screen.getByRole('button', { name: 'Add filter' })).toBeEnabled();
  });

  it('shows an error message when adding a filter fails', async () => {
    mockedCreateUpdateFilter.mockRejectedValue(new ApiError(400, 'Invalid regular expression: too many )'));
    const user = userEvent.setup();
    await openUpdateFiltersTab(user);

    await user.type(screen.getByLabelText('Name'), 'Broken');
    await user.type(screen.getByLabelText('Pattern (regular expression)'), ')');
    await user.click(screen.getByRole('button', { name: 'Add filter' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid regular expression: too many )');
  });

  it('edits a filter in place and saves the change', async () => {
    mockedListUpdateFilters.mockResolvedValue([
      { id: 1, name: 'Defender', pattern: 'Security Intelligence-Update', createdAt: '2026-01-01T00:00:00Z' },
    ]);
    mockedUpdateUpdateFilter.mockResolvedValue({ id: 1, name: 'Defender (renamed)', pattern: 'Security Intelligence-Update', createdAt: '2026-01-01T00:00:00Z' });
    const user = userEvent.setup();
    await openUpdateFiltersTab(user);
    await screen.findByText('Defender');

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    // Two "Name" fields exist while editing (this row's inline input and
    // the separate "Add a filter" form's own Name field below it) — found
    // unambiguously by its current value instead, since only the inline
    // one starts pre-filled with the filter's existing name.
    const nameInput = screen.getByDisplayValue('Defender');
    await user.clear(nameInput);
    await user.type(nameInput, 'Defender (renamed)');
    mockedListUpdateFilters.mockResolvedValue([
      { id: 1, name: 'Defender (renamed)', pattern: 'Security Intelligence-Update', createdAt: '2026-01-01T00:00:00Z' },
    ]);
    // Two "Save" buttons exist while editing — this row's own Save and the
    // page's main settings-form submit button below it; the row's own
    // renders first.
    await user.click(screen.getAllByRole('button', { name: 'Save' })[0]);

    expect(mockedUpdateUpdateFilter).toHaveBeenCalledWith(1, { name: 'Defender (renamed)', pattern: 'Security Intelligence-Update' });
    expect(await screen.findByText('Defender (renamed)')).toBeInTheDocument();
  });

  it('cancels an in-place edit without saving', async () => {
    mockedListUpdateFilters.mockResolvedValue([
      { id: 1, name: 'Defender', pattern: 'Security Intelligence-Update', createdAt: '2026-01-01T00:00:00Z' },
    ]);
    const user = userEvent.setup();
    await openUpdateFiltersTab(user);
    await screen.findByText('Defender');

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(mockedUpdateUpdateFilter).not.toHaveBeenCalled();
    expect(screen.getByText('Defender')).toBeInTheDocument();
  });

  it('deletes a filter after confirmation and reloads the list', async () => {
    mockedListUpdateFilters.mockResolvedValue([
      { id: 1, name: 'Defender', pattern: 'Security Intelligence-Update', createdAt: '2026-01-01T00:00:00Z' },
    ]);
    mockedDeleteUpdateFilter.mockResolvedValue(undefined);
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    const user = userEvent.setup();
    await openUpdateFiltersTab(user);
    await screen.findByText('Defender');
    mockedListUpdateFilters.mockResolvedValue([]);

    await user.click(screen.getByRole('button', { name: 'Delete' }));

    expect(confirmSpy).toHaveBeenCalledWith(expect.stringContaining('Defender'));
    expect(mockedDeleteUpdateFilter).toHaveBeenCalledWith(1);
    expect(await screen.findByText('No filters defined.')).toBeInTheDocument();
  });

  it('does nothing when delete confirmation is declined', async () => {
    mockedListUpdateFilters.mockResolvedValue([
      { id: 1, name: 'Defender', pattern: 'Security Intelligence-Update', createdAt: '2026-01-01T00:00:00Z' },
    ]);
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const user = userEvent.setup();
    await openUpdateFiltersTab(user);
    await screen.findByText('Defender');

    await user.click(screen.getByRole('button', { name: 'Delete' }));

    expect(mockedDeleteUpdateFilter).not.toHaveBeenCalled();
  });
});
