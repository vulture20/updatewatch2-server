import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { adminApi, versionApi } from '../api/endpoints';
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
}));

const mockedGetSettings = vi.mocked(adminApi.getSettings);
const mockedUpdateSettings = vi.mocked(adminApi.updateSettings);
const mockedGetVersion = vi.mocked(versionApi.get);

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
};

describe('AdminPage', () => {
  beforeEach(() => {
    mockedGetSettings.mockReset().mockResolvedValue(baseSettings);
    mockedUpdateSettings.mockReset();
    mockedGetVersion.mockReset().mockResolvedValue({ server: '0.3.0', protocol: '0.1.0', database: '0.3.0' });
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
    await user.type(await screen.findByLabelText('SMTP password'), 'new-secret');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('status');
    expect(mockedUpdateSettings).toHaveBeenCalledWith(expect.objectContaining({ smtpPassword: 'new-secret' }));
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
