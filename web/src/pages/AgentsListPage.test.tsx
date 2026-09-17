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
    installMany: vi.fn(),
    rebootMany: vi.fn(),
    deleteMany: vi.fn(),
  },
}));

const mockedList = vi.mocked(agentsApi.list);
const mockedApproveMany = vi.mocked(agentsApi.approveMany);
const mockedInstallMany = vi.mocked(agentsApi.installMany);
const mockedRebootMany = vi.mocked(agentsApi.rebootMany);
const mockedDeleteMany = vi.mocked(agentsApi.deleteMany);

function makeAgent(overrides: Partial<AgentListItem> & { hostname: string }): AgentListItem {
  return {
    approved: true,
    rebootRequired: false,
    pendingUpdateCount: 0,
    lastCertificateRejectionReason: null,
    operatingSystem: null,
    lastAliveAt: null,
    isOffline: false,
    pendingInstallRequestedAt: null,
    pendingRebootRequestedAt: null,
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
    mockedInstallMany.mockReset();
    mockedRebootMany.mockReset();
    mockedDeleteMany.mockReset();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
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

  it('shows no status badge at all for an approved, idle agent', async () => {
    mockedList.mockResolvedValue([makeAgent({ hostname: 'idle-host' })]);

    renderPage();

    const row = (await screen.findByText('idle-host')).closest('tr');
    expect(row && within(row).queryByText('Approved')).not.toBeInTheDocument();
    expect(row && within(row).queryByText('Unapproved')).not.toBeInTheDocument();
    expect(row && within(row).queryByText('Updates')).not.toBeInTheDocument();
    expect(row && within(row).queryByText('Reboot')).not.toBeInTheDocument();
  });

  it('shows an "Unapproved" badge for an agent awaiting approval', async () => {
    mockedList.mockResolvedValue([makeAgent({ hostname: 'unapproved-host', approved: false })]);

    renderPage();

    const row = (await screen.findByText('unapproved-host')).closest('tr');
    expect(row && within(row).getByText('Unapproved')).toBeInTheDocument();
  });

  it('shows an "Updates" badge while an install is pending', async () => {
    mockedList.mockResolvedValue([
      makeAgent({ hostname: 'installing-host', pendingInstallRequestedAt: '2026-09-16T10:00:00Z' }),
    ]);

    renderPage();

    const row = (await screen.findByText('installing-host')).closest('tr');
    expect(row && within(row).getByText('Updates')).toBeInTheDocument();
  });

  it('shows a "Reboot" badge while a reboot is pending, taking priority over a simultaneously pending install', async () => {
    mockedList.mockResolvedValue([
      makeAgent({
        hostname: 'rebooting-host',
        pendingInstallRequestedAt: '2026-09-16T10:00:00Z',
        pendingRebootRequestedAt: '2026-09-16T10:05:00Z',
      }),
    ]);

    renderPage();

    const row = (await screen.findByText('rebooting-host')).closest('tr');
    expect(row && within(row).getByText('Reboot')).toBeInTheDocument();
    expect(row && within(row).queryByText('Updates')).not.toBeInTheDocument();
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

  it('installs updates for the selected agents with no confirmation dialog', async () => {
    mockedList.mockResolvedValue([makeAgent({ hostname: 'host-1' })]);
    mockedInstallMany.mockResolvedValue({ triggeredCount: 1, notFoundHostnames: [] });
    const user = userEvent.setup();

    renderPage();

    await screen.findByText('host-1');
    await user.click(screen.getByLabelText('select host-1'));
    await user.click(screen.getByRole('button', { name: /install updates/i }));

    await waitFor(() => expect(mockedInstallMany).toHaveBeenCalledWith(['host-1']));
    expect(window.confirm).not.toHaveBeenCalled();
  });

  it('reboots the selected agents after confirming', async () => {
    mockedList.mockResolvedValue([makeAgent({ hostname: 'host-1' })]);
    mockedRebootMany.mockResolvedValue({ triggeredCount: 1, notFoundHostnames: [] });
    const user = userEvent.setup();

    renderPage();

    await screen.findByText('host-1');
    await user.click(screen.getByLabelText('select host-1'));
    await user.click(screen.getByRole('button', { name: /reboot selected/i }));

    await waitFor(() => expect(mockedRebootMany).toHaveBeenCalledWith(['host-1']));
    expect(window.confirm).toHaveBeenCalled();
  });

  it('does not reboot the selected agents when the confirmation dialog is declined', async () => {
    mockedList.mockResolvedValue([makeAgent({ hostname: 'host-1' })]);
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const user = userEvent.setup();

    renderPage();

    await screen.findByText('host-1');
    await user.click(screen.getByLabelText('select host-1'));
    await user.click(screen.getByRole('button', { name: /reboot selected/i }));

    expect(mockedRebootMany).not.toHaveBeenCalled();
  });

  it('deletes the selected agents after confirming', async () => {
    mockedList.mockResolvedValue([makeAgent({ hostname: 'host-1' })]);
    mockedDeleteMany.mockResolvedValue({ deletedCount: 1, notFoundHostnames: [] });
    const user = userEvent.setup();

    renderPage();

    await screen.findByText('host-1');
    await user.click(screen.getByLabelText('select host-1'));
    await user.click(screen.getByRole('button', { name: /delete selected/i }));

    await waitFor(() => expect(mockedDeleteMany).toHaveBeenCalledWith(['host-1']));
    expect(window.confirm).toHaveBeenCalled();
  });

  it('disables the bulk action buttons until at least one agent is selected', async () => {
    mockedList.mockResolvedValue([makeAgent({ hostname: 'host-1' })]);

    renderPage();
    await screen.findByText('host-1');

    expect(screen.getByRole('button', { name: /approve selected/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /reboot selected/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /install updates/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /delete selected/i })).toBeDisabled();
  });

  it('selects and deselects every visible row via the header checkbox', async () => {
    mockedList.mockResolvedValue([makeAgent({ hostname: 'host-1' }), makeAgent({ hostname: 'host-2' })]);
    const user = userEvent.setup();

    renderPage();
    await screen.findByText('host-1');

    const selectAll = screen.getByLabelText('Select all');
    await user.click(selectAll);

    expect(screen.getByLabelText('select host-1')).toBeChecked();
    expect(screen.getByLabelText('select host-2')).toBeChecked();
    expect(screen.getByRole('button', { name: /approve selected/i })).toHaveTextContent('(2)');

    await user.click(selectAll);

    expect(screen.getByLabelText('select host-1')).not.toBeChecked();
    expect(screen.getByLabelText('select host-2')).not.toBeChecked();
  });

  it('only selects the currently filtered rows via the header checkbox, leaving hidden rows untouched', async () => {
    mockedList.mockResolvedValue([
      makeAgent({ hostname: 'win-host', operatingSystem: 'Windows Server 2022' }),
      makeAgent({ hostname: 'linux-host', operatingSystem: 'Ubuntu 22.04 LTS' }),
    ]);
    const user = userEvent.setup();

    renderPage();
    await screen.findByText('win-host');

    await user.selectOptions(screen.getByLabelText('OS type'), 'linux');
    await user.click(screen.getByLabelText('Select all'));

    expect(screen.getByLabelText('select linux-host')).toBeChecked();

    await user.click(screen.getByRole('button', { name: /clear filters/i }));
    expect(screen.getByLabelText('select win-host')).not.toBeChecked();
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

  it('filters by hostname as the user types in the search box', async () => {
    const user = userEvent.setup();
    mockedList.mockResolvedValue([
      makeAgent({ hostname: 'web-server-1' }),
      makeAgent({ hostname: 'web-server-2' }),
      makeAgent({ hostname: 'db-server-1' }),
    ]);

    renderPage();
    await screen.findByText('web-server-1');

    await user.type(screen.getByLabelText('Search'), 'web');

    expect(screen.getByText('web-server-1')).toBeInTheDocument();
    expect(screen.getByText('web-server-2')).toBeInTheDocument();
    expect(screen.queryByText('db-server-1')).not.toBeInTheDocument();

    // Case-insensitive.
    await user.clear(screen.getByLabelText('Search'));
    await user.type(screen.getByLabelText('Search'), 'DB-SERVER');
    expect(screen.getByText('db-server-1')).toBeInTheDocument();
    expect(screen.queryByText('web-server-1')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /clear filters/i }));
    expect(screen.getByText('web-server-1')).toBeInTheDocument();
    expect(screen.getByText('db-server-1')).toBeInTheDocument();
  });

  it('filters by the Status dropdown, matching the same states the status badge shows', async () => {
    const user = userEvent.setup();
    mockedList.mockResolvedValue([
      makeAgent({ hostname: 'idle-host' }),
      makeAgent({ hostname: 'unapproved-host', approved: false }),
      makeAgent({ hostname: 'installing-host', pendingInstallRequestedAt: '2026-09-16T10:00:00Z' }),
      makeAgent({ hostname: 'rebooting-host', pendingRebootRequestedAt: '2026-09-16T10:00:00Z' }),
    ]);

    renderPage();
    await screen.findByText('idle-host');

    await user.selectOptions(screen.getByLabelText('Status'), 'unapproved');
    expect(screen.getByText('unapproved-host')).toBeInTheDocument();
    expect(screen.queryByText('idle-host')).not.toBeInTheDocument();
    expect(screen.queryByText('installing-host')).not.toBeInTheDocument();
    expect(screen.queryByText('rebooting-host')).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Status'), 'installing');
    expect(screen.getByText('installing-host')).toBeInTheDocument();
    expect(screen.queryByText('unapproved-host')).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Status'), 'rebooting');
    expect(screen.getByText('rebooting-host')).toBeInTheDocument();
    expect(screen.queryByText('installing-host')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /clear filters/i }));
    expect(screen.getByText('idle-host')).toBeInTheDocument();
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

  it('filters by online status and marks an offline agent with the offline icon', async () => {
    const user = userEvent.setup();
    mockedList.mockResolvedValue([
      makeAgent({ hostname: 'online-host', isOffline: false }),
      makeAgent({ hostname: 'offline-host', isOffline: true }),
    ]);

    renderPage();
    await screen.findByText('online-host');

    expect(screen.getByTitle(/heartbeat/i)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Online status'), 'offline');

    expect(screen.queryByText('online-host')).not.toBeInTheDocument();
    expect(screen.getByText('offline-host')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /clear filters/i }));
    expect(screen.getByText('online-host')).toBeInTheDocument();
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
