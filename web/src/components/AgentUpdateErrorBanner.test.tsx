import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { agentUpdatesApi } from '../api/endpoints';
import { AgentUpdateErrorBanner } from './AgentUpdateErrorBanner';

vi.mock('../api/endpoints', () => ({
  agentUpdatesApi: {
    getStatus: vi.fn(),
  },
}));

const mockedGetStatus = vi.mocked(agentUpdatesApi.getStatus);

function renderBanner() {
  return render(
    <MemoryRouter>
      <AgentUpdateErrorBanner />
    </MemoryRouter>,
  );
}

describe('AgentUpdateErrorBanner', () => {
  beforeEach(() => {
    mockedGetStatus.mockReset();
  });

  it('shows nothing when auto-update is enabled and the last check succeeded', async () => {
    mockedGetStatus.mockResolvedValue({ enabled: true, latestVersion: '0.13.0', checkedAt: '2026-01-01T00:00:00Z', lastError: null, manuallyUploaded: false });

    renderBanner();

    await waitFor(() => expect(mockedGetStatus).toHaveBeenCalled());
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows a warning with the error when enabled and the last check failed', async () => {
    mockedGetStatus.mockResolvedValue({
      enabled: true,
      latestVersion: null,
      checkedAt: '2026-01-01T00:00:00Z',
      lastError: 'No such host is known.',
      manuallyUploaded: false,
    });

    renderBanner();

    expect(await screen.findByRole('alert')).toHaveTextContent('No such host is known.');
  });

  it('stays hidden when auto-update is disabled, even if lastError is set', async () => {
    // A disabled feature's own background check still runs and still
    // records a failure — this must not surface as a banner, since a
    // server intentionally relying on the manual-upload escape hatch
    // (CLAUDE.md's "Agent auto-update" bullet) would otherwise see a
    // permanent, misleading warning.
    mockedGetStatus.mockResolvedValue({
      enabled: false,
      latestVersion: null,
      checkedAt: '2026-01-01T00:00:00Z',
      lastError: 'No such host is known.',
      manuallyUploaded: false,
    });

    renderBanner();

    await waitFor(() => expect(mockedGetStatus).toHaveBeenCalled());
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows nothing when the status request fails (e.g. no admin session yet)', async () => {
    mockedGetStatus.mockRejectedValue(new Error('unauthorized'));

    renderBanner();

    await waitFor(() => expect(mockedGetStatus).toHaveBeenCalled());
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('polls periodically, so a failure that starts happening elsewhere shows up without a manual reload', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      mockedGetStatus
        .mockResolvedValueOnce({ enabled: true, latestVersion: '0.13.0', checkedAt: '2026-01-01T00:00:00Z', lastError: null, manuallyUploaded: false })
        .mockResolvedValueOnce({
          enabled: true,
          latestVersion: '0.13.0',
          checkedAt: '2026-01-01T01:00:00Z',
          lastError: 'GitHub rate limit exceeded.',
          manuallyUploaded: false,
        });

      renderBanner();

      await waitFor(() => expect(mockedGetStatus).toHaveBeenCalledTimes(1));
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();

      await vi.advanceTimersByTimeAsync(15000);

      await waitFor(() => expect(mockedGetStatus).toHaveBeenCalledTimes(2));
      expect(await screen.findByRole('alert')).toHaveTextContent('GitHub rate limit exceeded.');
    } finally {
      vi.useRealTimers();
    }
  });
});
