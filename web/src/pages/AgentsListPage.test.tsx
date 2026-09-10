import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { agentsApi } from '../api/endpoints';
import type { AgentListItem } from '../api/types';
import { AgentsListPage } from './AgentsListPage';

vi.mock('../api/endpoints', () => ({
  agentsApi: {
    list: vi.fn(),
    approveMany: vi.fn(),
  },
}));

const mockedList = vi.mocked(agentsApi.list);
const mockedApproveMany = vi.mocked(agentsApi.approveMany);

function makeAgent(overrides: Partial<AgentListItem> & { hostname: string }): AgentListItem {
  return {
    approved: true,
    rebootRequired: false,
    pendingUpdateCount: 0,
    lastCertificateRejectionReason: null,
    operatingSystem: null,
    lastAliveAt: null,
    ...overrides,
  };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <AgentsListPage />
    </MemoryRouter>,
  );
}

describe('AgentsListPage', () => {
  beforeEach(() => {
    mockedList.mockReset();
    mockedApproveMany.mockReset();
  });

  it('renders the agents returned by the API', async () => {
    mockedList.mockResolvedValue([
      makeAgent({ hostname: 'host-1', pendingUpdateCount: 2 }),
      makeAgent({ hostname: 'host-2', approved: false, rebootRequired: true }),
    ]);

    renderPage();

    expect(await screen.findByText('host-1')).toBeInTheDocument();
    expect(screen.getByText('host-2')).toBeInTheDocument();
  });

  it('shows the empty state when there are no agents', async () => {
    mockedList.mockResolvedValue([]);

    renderPage();

    expect(await screen.findByText('No agents registered yet.')).toBeInTheDocument();
  });

  it('approves the selected agents and reloads the list', async () => {
    mockedList.mockResolvedValue([makeAgent({ hostname: 'host-1', approved: false })]);
    mockedApproveMany.mockResolvedValue({ approvedCount: 1, notFoundHostnames: [] });
    const user = userEvent.setup();

    renderPage();

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
        .mockResolvedValueOnce([makeAgent({ hostname: 'host-1', approved: false })])
        .mockResolvedValueOnce([makeAgent({ hostname: 'host-1', approved: false, pendingUpdateCount: 5 })]);

      renderPage();

      expect(await screen.findByText('host-1')).toBeInTheDocument();
      expect(mockedList).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(5000);

      await waitFor(() => expect(mockedList).toHaveBeenCalledTimes(2));
      const row = screen.getByText('host-1').closest('tr');
      expect(row && within(row).getByText('5')).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('does not replace an already-rendered list with the error state when a background poll fails', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      mockedList
        .mockResolvedValueOnce([makeAgent({ hostname: 'host-1', approved: false })])
        .mockRejectedValueOnce(new Error('transient network error'));

      renderPage();

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
      makeAgent({ hostname: 'host-1', lastCertificateRejectionReason: 'Expired' }),
      makeAgent({ hostname: 'host-2' }),
    ]);

    renderPage();

    await screen.findByText('host-1');
    expect(screen.getByRole('img', { name: /Expired/ })).toBeInTheDocument();
  });

  it('shows no warning icon when nothing was recently rejected', async () => {
    mockedList.mockResolvedValue([makeAgent({ hostname: 'host-1' })]);

    renderPage();

    await screen.findByText('host-1');
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('shows fleet-wide stat cards and toggles a filter when one is clicked', async () => {
    const user = userEvent.setup();
    mockedList.mockResolvedValue([
      makeAgent({ hostname: 'approved-host' }),
      makeAgent({ hostname: 'pending-host', approved: false }),
      makeAgent({ hostname: 'reboot-host', rebootRequired: true }),
    ]);

    renderPage();
    await screen.findByText('approved-host');

    expect(screen.getByText('3')).toBeInTheDocument(); // total agents stat
    expect(screen.getByText('pending-host')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /pending approval/i }));

    expect(screen.queryByText('approved-host')).not.toBeInTheDocument();
    expect(screen.getByText('pending-host')).toBeInTheDocument();
  });

  it('filters by the OS-type dropdown', async () => {
    const user = userEvent.setup();
    mockedList.mockResolvedValue([
      makeAgent({ hostname: 'win-host', operatingSystem: 'Windows Server 2022' }),
      makeAgent({ hostname: 'linux-host', operatingSystem: 'Ubuntu 22.04 LTS' }),
    ]);

    renderPage();
    await screen.findByText('win-host');

    await user.selectOptions(screen.getByLabelText('OS type'), 'linux');

    expect(screen.queryByText('win-host')).not.toBeInTheDocument();
    expect(screen.getByText('linux-host')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /clear filters/i }));
    expect(screen.getByText('win-host')).toBeInTheDocument();
  });

  it('sorts the table when a column header is clicked', async () => {
    const user = userEvent.setup();
    mockedList.mockResolvedValue([makeAgent({ hostname: 'bravo' }), makeAgent({ hostname: 'alpha' })]);

    renderPage();
    await screen.findByText('bravo');

    const hostnameHeader = screen.getByRole('button', { name: /^Hostname/ });
    await user.click(hostnameHeader);

    const hostnames = screen.getAllByRole('row').slice(1).map((row) => within(row).getAllByRole('cell')[1].textContent);
    expect(hostnames).toEqual(['alpha', 'bravo']);
  });
});
