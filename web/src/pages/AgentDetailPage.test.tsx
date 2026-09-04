import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { agentsApi } from '../api/endpoints';
import type { AgentDetail } from '../api/types';
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
  clientCertificateIssuedAt: '2026-01-01T00:00:00Z',
  clientCertificateExpiresAt: '2028-01-01T00:00:00Z',
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
