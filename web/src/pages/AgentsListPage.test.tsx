import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { agentsApi } from '../api/endpoints';
import { AgentsListPage } from './AgentsListPage';

vi.mock('../api/endpoints', () => ({
  agentsApi: {
    list: vi.fn(),
    approveMany: vi.fn(),
  },
}));

const mockedList = vi.mocked(agentsApi.list);
const mockedApproveMany = vi.mocked(agentsApi.approveMany);

describe('AgentsListPage', () => {
  beforeEach(() => {
    mockedList.mockReset();
    mockedApproveMany.mockReset();
  });

  it('renders the agents returned by the API', async () => {
    mockedList.mockResolvedValue([
      { hostname: 'host-1', approved: true, rebootRequired: false, pendingUpdateCount: 2, lastCertificateRejectionReason: null },
      { hostname: 'host-2', approved: false, rebootRequired: true, pendingUpdateCount: 0, lastCertificateRejectionReason: null },
    ]);

    render(
      <MemoryRouter>
        <AgentsListPage />
      </MemoryRouter>,
    );

    expect(await screen.findByText('host-1')).toBeInTheDocument();
    expect(screen.getByText('host-2')).toBeInTheDocument();
  });

  it('shows the empty state when there are no agents', async () => {
    mockedList.mockResolvedValue([]);

    render(
      <MemoryRouter>
        <AgentsListPage />
      </MemoryRouter>,
    );

    expect(await screen.findByText('No agents registered yet.')).toBeInTheDocument();
  });

  it('approves the selected agents and reloads the list', async () => {
    mockedList.mockResolvedValue([
      { hostname: 'host-1', approved: false, rebootRequired: false, pendingUpdateCount: 0, lastCertificateRejectionReason: null },
    ]);
    mockedApproveMany.mockResolvedValue({ approvedCount: 1, notFoundHostnames: [] });
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <AgentsListPage />
      </MemoryRouter>,
    );

    await screen.findByText('host-1');
    await user.click(screen.getByLabelText('select host-1'));
    await user.click(screen.getByRole('button', { name: /approve selected/i }));

    await waitFor(() => expect(mockedApproveMany).toHaveBeenCalledWith(['host-1']));
    expect(mockedList).toHaveBeenCalledTimes(2); // initial load + reload after approve
  });

  it('polls the list periodically, so a state change from elsewhere shows up without a manual reload', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      mockedList
        .mockResolvedValueOnce([{ hostname: 'host-1', approved: false, rebootRequired: false, pendingUpdateCount: 0, lastCertificateRejectionReason: null }])
        .mockResolvedValueOnce([{ hostname: 'host-1', approved: false, rebootRequired: false, pendingUpdateCount: 5, lastCertificateRejectionReason: null }]);

      render(
        <MemoryRouter>
          <AgentsListPage />
        </MemoryRouter>,
      );

      expect(await screen.findByText('host-1')).toBeInTheDocument();
      expect(mockedList).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(5000);

      await waitFor(() => expect(mockedList).toHaveBeenCalledTimes(2));
      expect(await screen.findByText('5')).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('does not replace an already-rendered list with the error state when a background poll fails', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      mockedList
        .mockResolvedValueOnce([{ hostname: 'host-1', approved: false, rebootRequired: false, pendingUpdateCount: 0, lastCertificateRejectionReason: null }])
        .mockRejectedValueOnce(new Error('transient network error'));

      render(
        <MemoryRouter>
          <AgentsListPage />
        </MemoryRouter>,
      );

      expect(await screen.findByText('host-1')).toBeInTheDocument();

      await vi.advanceTimersByTimeAsync(5000);
      await waitFor(() => expect(mockedList).toHaveBeenCalledTimes(2));

      // Still showing the last known-good list, not the hard error state.
      expect(screen.getByText('host-1')).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('flags a row with a warning icon when the agent recently presented a rejected certificate', async () => {
    mockedList.mockResolvedValue([
      { hostname: 'host-1', approved: true, rebootRequired: false, pendingUpdateCount: 0, lastCertificateRejectionReason: 'Expired' },
      { hostname: 'host-2', approved: true, rebootRequired: false, pendingUpdateCount: 0, lastCertificateRejectionReason: null },
    ]);

    render(
      <MemoryRouter>
        <AgentsListPage />
      </MemoryRouter>,
    );

    await screen.findByText('host-1');
    expect(screen.getByRole('img', { name: /Expired/ })).toBeInTheDocument();
  });

  it('shows no warning icon when nothing was recently rejected', async () => {
    mockedList.mockResolvedValue([
      { hostname: 'host-1', approved: true, rebootRequired: false, pendingUpdateCount: 0, lastCertificateRejectionReason: null },
    ]);

    render(
      <MemoryRouter>
        <AgentsListPage />
      </MemoryRouter>,
    );

    await screen.findByText('host-1');
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });
});
