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
    delete: vi.fn(),
  },
}));

const mockedGet = vi.mocked(agentsApi.get);
const mockedUpdates = vi.mocked(agentsApi.updates);
const mockedReissueCertificate = vi.mocked(agentsApi.reissueCertificate);
const mockedTriggerInstall = vi.mocked(agentsApi.triggerInstall);
const mockedDelete = vi.mocked(agentsApi.delete);

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
  issuingRootThumbprint: 'root-thumb-1',
  lastCertificateRejectionReason: null,
  lastCertificateRejectionAt: null,
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/agents/host-1']}>
      <Routes>
        <Route path="/agents/:hostname" element={<AgentDetailPage />} />
        <Route path="/agents" element={<div>agents list page</div>} />
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

  it('shows the issuing CA root thumbprint', async () => {
    mockedGet.mockResolvedValue(approvedAgent);

    renderPage();

    expect(await screen.findByText('root-thumb-1')).toBeInTheDocument();
  });

  it('shows a placeholder when the issuing CA root is unknown', async () => {
    mockedGet.mockResolvedValue({ ...approvedAgent, issuingRootThumbprint: null });

    renderPage();

    await screen.findByText('abc123');
    expect(screen.getByText('Issuing CA root (SHA-256)').nextElementSibling).toHaveTextContent('—');
  });

  it('shows the certificate rejection reason and timestamp when one is recorded', async () => {
    mockedGet.mockResolvedValue({
      ...approvedAgent,
      lastCertificateRejectionReason: 'Expired',
      lastCertificateRejectionAt: '2026-01-05T12:00:00Z',
    });

    renderPage();

    expect(await screen.findByText(/Last certificate rejection/)).toBeInTheDocument();
    expect(screen.getByText(/Certificate expired/)).toBeInTheDocument();
  });

  it('does not show a certificate rejection row when there is none', async () => {
    mockedGet.mockResolvedValue(approvedAgent);

    renderPage();

    await screen.findByText('abc123');
    expect(screen.queryByText('Last certificate rejection')).not.toBeInTheDocument();
  });

  it('shows the reissue button only for an approved agent', async () => {
    mockedGet.mockResolvedValue({ ...approvedAgent, approved: false });

    renderPage();

    await screen.findByRole('heading', { name: 'host-1' });
    expect(screen.queryByRole('button', { name: /reissue certificate/i })).not.toBeInTheDocument();
  });

  it('reissues a certificate, shows the one-time token, and reloads on close', async () => {
    mockedGet.mockResolvedValue(approvedAgent);
    mockedReissueCertificate.mockResolvedValue({ registrationToken: 'fresh-token-value' });
    const user = userEvent.setup();

    renderPage();

    await screen.findByRole('heading', { name: 'host-1' });
    await user.click(screen.getByRole('button', { name: /reissue certificate/i }));

    expect(window.confirm).toHaveBeenCalled();
    await waitFor(() => expect(mockedReissueCertificate).toHaveBeenCalledWith('host-1'));
    expect(await screen.findByText('fresh-token-value')).toBeInTheDocument();
    // A real overlay dialog now, not an inline banner.
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /close/i }));

    expect(screen.queryByText('fresh-token-value')).not.toBeInTheDocument();
    await waitFor(() => expect(mockedGet).toHaveBeenCalledTimes(2)); // initial load + reload after close
  });

  it('does not call the API when the confirmation is declined', async () => {
    mockedGet.mockResolvedValue(approvedAgent);
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const user = userEvent.setup();

    renderPage();

    await screen.findByRole('heading', { name: 'host-1' });
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

    await screen.findByRole('heading', { name: 'host-1' });
    await user.click(screen.getByRole('button', { name: /install updates now/i }));

    await waitFor(() => expect(mockedTriggerInstall).toHaveBeenCalledWith('host-1'));
    await waitFor(() => expect(mockedGet).toHaveBeenCalledTimes(2));
    expect(await screen.findByRole('button', { name: /install pending/i })).toBeDisabled();
  });

  it('disables the button and hides the trigger label while an install is already pending', async () => {
    mockedGet.mockResolvedValue({ ...approvedAgent, pendingInstallRequestedAt: '2026-01-02T00:00:00Z' });

    renderPage();

    await screen.findByRole('heading', { name: 'host-1' });
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

    await screen.findByRole('heading', { name: 'host-1' });
    expect(screen.getByText(/succeeded/i)).toBeInTheDocument();
  });
});

describe('AgentDetailPage layout', () => {
  beforeEach(() => {
    mockedGet.mockReset();
    mockedUpdates.mockReset();
  });

  it('splits agent info into three cards', async () => {
    mockedGet.mockResolvedValue(approvedAgent);
    mockedUpdates.mockResolvedValue([]);

    renderPage();

    await screen.findByRole('heading', { name: 'host-1' });
    expect(screen.getByText('Identity')).toBeInTheDocument();
    expect(screen.getByText('Certificate')).toBeInTheDocument();
    expect(screen.getByText('Install status')).toBeInTheDocument();
  });

  it('sorts the pending updates table when a column header is clicked', async () => {
    mockedGet.mockResolvedValue(approvedAgent);
    mockedUpdates.mockResolvedValue([
      { id: 1, title: 'Bravo update', packageId: null, description: null, detectedAt: '2026-01-01T00:00:00Z', installed: false },
      { id: 2, title: 'Alpha update', packageId: null, description: null, detectedAt: '2026-01-02T00:00:00Z', installed: false },
    ]);
    const user = userEvent.setup();

    renderPage();

    await screen.findByText('Bravo update');
    await user.click(screen.getByRole('button', { name: /^Title/ }));

    const titles = screen.getAllByRole('row').slice(1).map((row) => row.querySelector('td')?.textContent);
    expect(titles).toEqual(['Alpha update', 'Bravo update']);
  });
});

describe('AgentDetailPage polling', () => {
  beforeEach(() => {
    mockedGet.mockReset();
    mockedUpdates.mockReset();
    mockedUpdates.mockResolvedValue([]);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('polls periodically, so a certificate arriving after approval shows up without a manual reload', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    mockedGet
      .mockResolvedValueOnce({ ...approvedAgent, clientCertificateThumbprint: null })
      .mockResolvedValueOnce(approvedAgent);

    renderPage();

    expect(await screen.findByRole('heading', { name: 'host-1' })).toBeInTheDocument();
    expect(mockedGet).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(5000);

    await waitFor(() => expect(mockedGet).toHaveBeenCalledTimes(2));
    expect(await screen.findByText('abc123')).toBeInTheDocument();
  });

  it('does not replace an already-rendered agent with the not-found state when a background poll fails', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    mockedGet.mockResolvedValueOnce(approvedAgent).mockRejectedValueOnce(new Error('transient network error'));

    renderPage();

    expect(await screen.findByRole('heading', { name: 'host-1' })).toBeInTheDocument();

    await vi.advanceTimersByTimeAsync(5000);
    await waitFor(() => expect(mockedGet).toHaveBeenCalledTimes(2));

    expect(screen.getByRole('heading', { name: 'host-1' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});

describe('AgentDetailPage deletion', () => {
  beforeEach(() => {
    mockedGet.mockReset();
    mockedUpdates.mockReset();
    mockedDelete.mockReset();
    mockedUpdates.mockResolvedValue([]);
    mockedGet.mockResolvedValue(approvedAgent);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('deletes the agent after confirmation and navigates back to the agents list', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    mockedDelete.mockResolvedValue(undefined);
    const user = userEvent.setup();

    renderPage();

    await screen.findByRole('heading', { name: 'host-1' });
    await user.click(screen.getByRole('button', { name: /delete agent/i }));

    expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining('host-1'));
    await waitFor(() => expect(mockedDelete).toHaveBeenCalledWith('host-1'));
    expect(await screen.findByText('agents list page')).toBeInTheDocument();
  });

  it('does not delete or navigate when the confirmation is declined', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const user = userEvent.setup();

    renderPage();

    await screen.findByRole('heading', { name: 'host-1' });
    await user.click(screen.getByRole('button', { name: /delete agent/i }));

    expect(mockedDelete).not.toHaveBeenCalled();
    expect(screen.getByRole('heading', { name: 'host-1' })).toBeInTheDocument();
  });
});
