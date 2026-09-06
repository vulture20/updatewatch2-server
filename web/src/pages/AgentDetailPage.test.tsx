import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { agentsApi } from '../api/endpoints';
import type { AgentDetail, UpdateItem } from '../api/types';
import { AgentDetailPage } from './AgentDetailPage';

vi.mock('../api/endpoints', () => ({
  agentsApi: {
    get: vi.fn(),
    updates: vi.fn(),
    approve: vi.fn(),
    triggerInstall: vi.fn(),
    reissueCertificate: vi.fn(),
  },
}));

const mockedGet = vi.mocked(agentsApi.get);
const mockedUpdates = vi.mocked(agentsApi.updates);
const mockedReissueCertificate = vi.mocked(agentsApi.reissueCertificate);
const mockedTriggerInstall = vi.mocked(agentsApi.triggerInstall);

const pendingUpdate: UpdateItem = {
  id: 1,
  title: 'Security Update',
  packageId: 'KB123456',
  description: null,
  detectedAt: '2026-01-01T00:00:00Z',
  installed: false,
};

const approvedAgent: AgentDetail = {
  hostname: 'host-1',
  dnsName: 'host-1.example.com',
  operatingSystem: 'Windows Server 2022',
  ipAddress: '10.0.0.5',
  agentVersion: '0.4.0',
  approved: true,
  rebootRequired: false,
  pendingUpdateCount: 0,
  lastAliveAt: null,
  clientCertificateThumbprint: 'abc123',
  clientCertificateThumbprintSha1: 'def456',
  clientCertificateIssuedAt: '2026-01-01T00:00:00Z',
  clientCertificateExpiresAt: '2028-01-01T00:00:00Z',
  pendingInstallRequestedAt: null,
  lastInstallOutcome: null,
  lastInstallCompletedAt: null,
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/agents/host-1']}>
      <Routes>
        <Route path="/agents/:hostname" element={<AgentDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('AgentDetailPage certificate re-issuance', () => {
  beforeEach(() => {
    mockedGet.mockReset();
    mockedUpdates.mockReset();
    mockedReissueCertificate.mockReset();
    mockedTriggerInstall.mockReset();
    mockedUpdates.mockResolvedValue([]);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    // navigator.clipboard is a getter-only accessor in jsdom — a plain
    // Object.assign/property assignment throws ("has only a getter"),
    // regardless of whether it's the first test to touch it. defineProperty
    // replaces the accessor outright instead of going through it.
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
      configurable: true,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('shows both the SHA-256 and SHA-1 certificate thumbprints', async () => {
    mockedGet.mockResolvedValue(approvedAgent);

    renderPage();

    expect(await screen.findByText('abc123')).toBeInTheDocument();
    expect(screen.getByText('def456')).toBeInTheDocument();
  });

  it('shows the reissue button only for an approved agent', async () => {
    mockedGet.mockResolvedValue({ ...approvedAgent, approved: false });

    renderPage();

    await screen.findByText('host-1');
    expect(screen.queryByRole('button', { name: /reissue certificate/i })).not.toBeInTheDocument();
  });

  it('reissues a certificate, shows the one-time token, and reloads on close', async () => {
    mockedGet.mockResolvedValue(approvedAgent);
    mockedReissueCertificate.mockResolvedValue({ registrationToken: 'fresh-token-value' });
    const user = userEvent.setup();

    renderPage();

    await screen.findByText('host-1');
    await user.click(screen.getByRole('button', { name: /reissue certificate/i }));

    expect(window.confirm).toHaveBeenCalled();
    await waitFor(() => expect(mockedReissueCertificate).toHaveBeenCalledWith('host-1'));
    expect(await screen.findByText('fresh-token-value')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /close/i }));

    expect(screen.queryByText('fresh-token-value')).not.toBeInTheDocument();
    await waitFor(() => expect(mockedGet).toHaveBeenCalledTimes(2)); // initial load + reload after close
  });

  it('does not call the API when the confirmation is declined', async () => {
    mockedGet.mockResolvedValue(approvedAgent);
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const user = userEvent.setup();

    renderPage();

    await screen.findByText('host-1');
    await user.click(screen.getByRole('button', { name: /reissue certificate/i }));

    expect(mockedReissueCertificate).not.toHaveBeenCalled();
  });
});

// updatewatch2-server#10: the trigger-install button used to be pure
// fire-and-forget (no re-fetch, no pending/outcome state shown) — the
// issue that split out remote-install delivery called this out explicitly
// as something a real delivery mechanism should fix.
describe('AgentDetailPage install trigger', () => {
  beforeEach(() => {
    mockedGet.mockReset();
    mockedUpdates.mockReset();
    mockedTriggerInstall.mockReset();
    mockedUpdates.mockResolvedValue([pendingUpdate]);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('triggers install and reloads the agent afterward', async () => {
    mockedGet.mockResolvedValueOnce(approvedAgent).mockResolvedValueOnce({
      ...approvedAgent,
      pendingInstallRequestedAt: '2026-01-02T00:00:00Z',
    });
    mockedTriggerInstall.mockResolvedValue(undefined);
    const user = userEvent.setup();

    renderPage();

    await screen.findByText('host-1');
    await user.click(screen.getByRole('button', { name: /install updates now/i }));

    await waitFor(() => expect(mockedTriggerInstall).toHaveBeenCalledWith('host-1'));
    await waitFor(() => expect(mockedGet).toHaveBeenCalledTimes(2));
    expect(await screen.findByRole('button', { name: /install pending/i })).toBeDisabled();
  });

  it('disables the button and hides the trigger label while an install is already pending', async () => {
    mockedGet.mockResolvedValue({ ...approvedAgent, pendingInstallRequestedAt: '2026-01-02T00:00:00Z' });

    renderPage();

    await screen.findByText('host-1');
    expect(screen.queryByRole('button', { name: /install updates now/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /install pending/i })).toBeDisabled();
  });

  it('shows the last install outcome once acknowledged', async () => {
    mockedGet.mockResolvedValue({
      ...approvedAgent,
      lastInstallOutcome: 'Succeeded',
      lastInstallCompletedAt: '2026-01-02T00:00:00Z',
    });

    renderPage();

    await screen.findByText('host-1');
    expect(screen.getByText(/succeeded/i)).toBeInTheDocument();
  });
});
