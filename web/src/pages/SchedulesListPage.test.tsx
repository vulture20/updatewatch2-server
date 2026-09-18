import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { agentsApi, schedulesApi } from '../api/endpoints';
import type { AgentListItem, Schedule } from '../api/types';
import { SchedulesListPage } from './SchedulesListPage';

vi.mock('../api/endpoints', () => ({
  schedulesApi: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
    runNow: vi.fn(),
    getRuns: vi.fn(),
  },
  agentsApi: {
    list: vi.fn(),
  },
}));

const mockedList = vi.mocked(schedulesApi.list);
const mockedCreate = vi.mocked(schedulesApi.create);
const mockedDelete = vi.mocked(schedulesApi.delete);
const mockedRunNow = vi.mocked(schedulesApi.runNow);
const mockedAgentsList = vi.mocked(agentsApi.list);

function makeSchedule(overrides: Partial<Schedule> & { id: number; name: string }): Schedule {
  return {
    enabled: true,
    status: 'Active',
    scheduleType: 'Once',
    pattern: null,
    onceAt: '2026-12-01T02:00:00Z',
    weeklyDays: null,
    timeOfDay: '02:00:00',
    intervalDays: null,
    intervalStartDate: null,
    actionInstall: true,
    actionReboot: false,
    rebootOnlyIfRequired: false,
    deadlineHours: 4,
    nextRunAt: '2026-12-01T02:00:00Z',
    lastRunAt: null,
    hostnames: ['host-1'],
    ...overrides,
  };
}

function makeAgent(hostname: string): AgentListItem {
  return {
    hostname,
    approved: true,
    rebootRequired: false,
    pendingUpdateCount: 0,
    lastCertificateRejectionReason: null,
    operatingSystem: null,
    lastAliveAt: null,
    isOffline: false,
    pendingInstallRequestedAt: null,
    pendingRebootRequestedAt: null,
  };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <SchedulesListPage />
    </MemoryRouter>,
  );
}

describe('SchedulesListPage', () => {
  beforeEach(() => {
    mockedList.mockReset();
    mockedCreate.mockReset();
    mockedDelete.mockReset();
    mockedRunNow.mockReset();
    mockedAgentsList.mockReset();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
  });

  it('renders the schedules returned by the API', async () => {
    mockedList.mockResolvedValue([makeSchedule({ id: 1, name: 'Nightly install' })]);

    renderPage();

    expect(await screen.findByText('Nightly install')).toBeInTheDocument();
  });

  it('shows the empty state when there are no schedules', async () => {
    mockedList.mockResolvedValue([]);

    renderPage();

    expect(await screen.findByText(/no schedules yet/i)).toBeInTheDocument();
  });

  it('opens the create dialog and lists agents to pick from', async () => {
    mockedList.mockResolvedValue([]);
    mockedAgentsList.mockResolvedValue([makeAgent('host-1'), makeAgent('host-2')]);
    const user = userEvent.setup();

    renderPage();
    await screen.findByText(/no schedules yet/i);
    await user.click(screen.getByRole('button', { name: /new schedule/i }));

    expect(await screen.findByText('host-1')).toBeInTheDocument();
    expect(screen.getByText('host-2')).toBeInTheDocument();
  });

  it('select-all-visible only checks agents matching the current search filter', async () => {
    mockedList.mockResolvedValue([]);
    mockedAgentsList.mockResolvedValue([makeAgent('web-host'), makeAgent('web-host-2'), makeAgent('db-host')]);
    const user = userEvent.setup();

    renderPage();
    await screen.findByText(/no schedules yet/i);
    await user.click(screen.getByRole('button', { name: /new schedule/i }));
    await screen.findByText('web-host');

    await user.type(screen.getByPlaceholderText(/search agents/i), 'web');
    expect(screen.queryByText('db-host')).not.toBeInTheDocument();

    await user.click(screen.getByRole('checkbox', { name: /select all visible/i }));

    expect(screen.getByLabelText('web-host')).toBeChecked();
    expect(screen.getByLabelText('web-host-2')).toBeChecked();

    // Clearing the filter reveals the previously-hidden agent, untouched —
    // not all visible are selected anymore, so the checkbox now selects
    // (not deselects) on the next click, including the newly-visible one.
    await user.clear(screen.getByPlaceholderText(/search agents/i));
    expect(screen.getByLabelText('db-host')).not.toBeChecked();

    await user.click(screen.getByRole('checkbox', { name: /select all visible/i }));
    expect(screen.getByLabelText('web-host')).toBeChecked();
    expect(screen.getByLabelText('web-host-2')).toBeChecked();
    expect(screen.getByLabelText('db-host')).toBeChecked();

    // Now every agent is selected, so the same checkbox deselects all.
    await user.click(screen.getByRole('checkbox', { name: /select all visible/i }));
    expect(screen.getByLabelText('web-host')).not.toBeChecked();
    expect(screen.getByLabelText('web-host-2')).not.toBeChecked();
    expect(screen.getByLabelText('db-host')).not.toBeChecked();
  });

  it('creates a schedule and reloads the list', async () => {
    mockedList.mockResolvedValueOnce([]).mockResolvedValueOnce([makeSchedule({ id: 1, name: 'New schedule' })]);
    mockedAgentsList.mockResolvedValue([makeAgent('host-1')]);
    mockedCreate.mockResolvedValue(makeSchedule({ id: 1, name: 'New schedule' }));
    const user = userEvent.setup();

    renderPage();
    await screen.findByText(/no schedules yet/i);
    await user.click(screen.getByRole('button', { name: /new schedule/i }));

    await screen.findByText('host-1');
    await user.type(screen.getByLabelText(/^name$/i), 'New schedule');
    await user.type(screen.getByLabelText(/date\/time/i), '2027-01-01T02:00');
    await user.click(screen.getByLabelText('host-1'));
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    await waitFor(() => expect(mockedCreate).toHaveBeenCalledTimes(1));
    expect(mockedCreate.mock.calls[0][0]).toMatchObject({ name: 'New schedule', hostnames: ['host-1'] });
    // Two matches exist once the dialog closes and the reloaded row
    // renders — the toolbar's own "New schedule" button, and this row's
    // Name cell — so disambiguate by role rather than plain text.
    await screen.findByRole('cell', { name: 'New schedule' });
  });

  it('deletes a schedule after confirmation', async () => {
    mockedList.mockResolvedValueOnce([makeSchedule({ id: 1, name: 'To delete' })]).mockResolvedValueOnce([]);
    mockedDelete.mockResolvedValue(undefined);
    const user = userEvent.setup();

    renderPage();
    await screen.findByText('To delete');
    await user.click(screen.getByRole('button', { name: /delete/i }));

    expect(window.confirm).toHaveBeenCalled();
    await waitFor(() => expect(mockedDelete).toHaveBeenCalledWith(1));
  });

  it('triggers run-now for a schedule', async () => {
    mockedList.mockResolvedValue([makeSchedule({ id: 1, name: 'Run me' })]);
    mockedRunNow.mockResolvedValue(undefined);
    const user = userEvent.setup();

    renderPage();
    await screen.findByText('Run me');
    await user.click(screen.getByRole('button', { name: /run now/i }));

    await waitFor(() => expect(mockedRunNow).toHaveBeenCalledWith(1));
  });
});
