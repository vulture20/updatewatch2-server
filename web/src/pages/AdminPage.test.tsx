import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { adminApi, agentUpdatesApi, certificateAuthorityApi, versionApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { AdminPage } from './AdminPage';

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
    prepareRotation: vi.fn(),
    activateRotation: vi.fn(),
    retirePreviousRoot: vi.fn(),
  },
  agentUpdatesApi: {
    getStatus: vi.fn(),
  },
}));

const mockedGetSettings = vi.mocked(adminApi.getSettings);
const mockedUpdateSettings = vi.mocked(adminApi.updateSettings);
const mockedGetVersion = vi.mocked(versionApi.get);
const mockedGetCaStatus = vi.mocked(certificateAuthorityApi.getStatus);
const mockedPrepareRotation = vi.mocked(certificateAuthorityApi.prepareRotation);
const mockedActivateRotation = vi.mocked(certificateAuthorityApi.activateRotation);
const mockedRetirePreviousRoot = vi.mocked(certificateAuthorityApi.retirePreviousRoot);
const mockedGetAgentUpdateStatus = vi.mocked(agentUpdatesApi.getStatus);

const baseCaStatus = {
  currentThumbprint: 'AAAA',
  currentNotAfter: '2036-01-01T00:00:00Z',
  previousThumbprint: null,
  previousNotAfter: null,
  pendingThumbprint: null,
  pendingNotAfter: null,
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
  });

  it('renders the loaded settings into the form fields', async () => {
    render(<AdminPage />);

    expect(await screen.findByLabelText('SMTP host')).toHaveValue('smtp.example.com');
    expect(screen.getByLabelText('Max attempts')).toHaveValue(6);
    expect(screen.getByLabelText('Log level')).toHaveValue('INFO');
    // The current password is never sent by the server — the field starts empty.
    expect(screen.getByLabelText('SMTP password')).toHaveValue('');
  });

  it('submits the edited form and shows a saved confirmation', async () => {
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, bruteForceMaxAttempts: 9 });
    const user = userEvent.setup();

    render(<AdminPage />);
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

    render(<AdminPage />);
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

    render(<AdminPage />);
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

    render(<AdminPage />);
    await screen.findByLabelText('SMTP host');
    await user.click(screen.getByRole('tab', { name: 'Certificates' }));

    const validity = screen.getByLabelText('Agent certificate validity (days)');
    await user.clear(validity);
    await user.type(validity, '90');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ agentCertificateValidityDays: 90 }));
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

    render(<AdminPage />);
    await screen.findByLabelText('SMTP host');

    expect(await screen.findByText('0.11.0')).toBeInTheDocument();

    await user.click(screen.getByLabelText('Check for and distribute new agent releases'));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ agentAutoUpdateEnabled: false }));
  });

  it('submits an edited agent auto-update check interval', async () => {
    mockedUpdateSettings.mockResolvedValue({ ...baseSettings, agentAutoUpdateCheckIntervalHours: 24 });
    const user = userEvent.setup();

    render(<AdminPage />);
    await screen.findByLabelText('SMTP host');

    const interval = screen.getByLabelText('Check interval (hours)');
    await user.clear(interval);
    await user.type(interval, '24');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ agentAutoUpdateCheckIntervalHours: 24 }));
  });

  it('sends a typed GitHub token as gitHubToken, and omits it when left blank', async () => {
    mockedUpdateSettings.mockResolvedValue(baseSettings);
    const user = userEvent.setup();

    render(<AdminPage />);
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

    render(<AdminPage />);
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

    render(<AdminPage />);
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
  });

  const openCertificatesTab = async (user: ReturnType<typeof userEvent.setup>) => {
    render(<AdminPage />);
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

  it('shows an error message when a rotation action fails', async () => {
    mockedPrepareRotation.mockRejectedValue(new ApiError(500, 'Something went wrong.'));
    const user = userEvent.setup();
    await openCertificatesTab(user);

    await user.click(await screen.findByRole('button', { name: 'Prepare rotation' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Something went wrong.');
  });
});
