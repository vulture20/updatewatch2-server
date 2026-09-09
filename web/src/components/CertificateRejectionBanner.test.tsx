import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { certificateRejectionsApi } from '../api/endpoints';
import { CertificateRejectionBanner } from './CertificateRejectionBanner';

vi.mock('../api/endpoints', () => ({
  certificateRejectionsApi: {
    getStatus: vi.fn(),
    acknowledge: vi.fn(),
  },
}));

const mockedGetStatus = vi.mocked(certificateRejectionsApi.getStatus);
const mockedAcknowledge = vi.mocked(certificateRejectionsApi.acknowledge);

describe('CertificateRejectionBanner', () => {
  beforeEach(() => {
    mockedGetStatus.mockReset();
    mockedAcknowledge.mockReset();
  });

  it('shows nothing when there are no recent rejections', async () => {
    mockedGetStatus.mockResolvedValue({ recentCount: 0, recent: [] });

    render(<CertificateRejectionBanner />);

    await waitFor(() => expect(mockedGetStatus).toHaveBeenCalled());
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows a warning with the count when there are recent rejections', async () => {
    mockedGetStatus.mockResolvedValue({
      recentCount: 3,
      recent: [{ timestamp: '2026-01-01T00:00:00Z', reason: 'Expired', details: 'Expired thumbprint=abc' }],
    });

    render(<CertificateRejectionBanner />);

    expect(await screen.findByRole('alert')).toHaveTextContent('3');
  });

  it('shows nothing when the status request fails (e.g. no admin session yet)', async () => {
    mockedGetStatus.mockRejectedValue(new Error('unauthorized'));

    render(<CertificateRejectionBanner />);

    await waitFor(() => expect(mockedGetStatus).toHaveBeenCalled());
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('polls periodically, so a rejection that starts happening elsewhere shows up without a manual reload', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      mockedGetStatus
        .mockResolvedValueOnce({ recentCount: 0, recent: [] })
        .mockResolvedValueOnce({ recentCount: 1, recent: [{ timestamp: '2026-01-01T00:00:00Z', reason: 'NotTrusted', details: null }] });

      render(<CertificateRejectionBanner />);

      await waitFor(() => expect(mockedGetStatus).toHaveBeenCalledTimes(1));
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();

      await vi.advanceTimersByTimeAsync(15000);

      await waitFor(() => expect(mockedGetStatus).toHaveBeenCalledTimes(2));
      expect(await screen.findByRole('alert')).toHaveTextContent('1');
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows an Acknowledge button that clears the banner on click', async () => {
    mockedGetStatus.mockResolvedValue({
      recentCount: 2,
      recent: [{ timestamp: '2026-01-01T00:00:00Z', reason: 'Expired', details: null }],
    });
    mockedAcknowledge.mockResolvedValue({ recentCount: 0, recent: [] });
    const user = userEvent.setup();

    render(<CertificateRejectionBanner />);
    await screen.findByRole('alert');

    await user.click(screen.getByRole('button', { name: 'Acknowledge' }));

    expect(mockedAcknowledge).toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  });

  it('keeps showing the banner if acknowledging fails', async () => {
    mockedGetStatus.mockResolvedValue({
      recentCount: 1,
      recent: [{ timestamp: '2026-01-01T00:00:00Z', reason: 'Expired', details: null }],
    });
    mockedAcknowledge.mockRejectedValue(new Error('network error'));
    const user = userEvent.setup();

    render(<CertificateRejectionBanner />);
    await screen.findByRole('alert');

    await user.click(screen.getByRole('button', { name: 'Acknowledge' }));

    expect(mockedAcknowledge).toHaveBeenCalled();
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });
});
