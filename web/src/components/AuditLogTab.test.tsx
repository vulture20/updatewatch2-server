import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { auditLogApi } from '../api/endpoints';
import { AuditLogTab } from './AuditLogTab';

vi.mock('../api/endpoints', () => ({
  auditLogApi: {
    getPage: vi.fn(),
  },
}));

const mockedGetPage = vi.mocked(auditLogApi.getPage);

describe('AuditLogTab', () => {
  beforeEach(() => {
    mockedGetPage.mockReset();
  });

  it('renders the entries returned by the API', async () => {
    mockedGetPage.mockResolvedValue({
      entries: [{ id: 1, timestamp: '2026-01-01T00:00:00Z', actor: 'admin', action: 'agent.approve', details: 'host-1' }],
      totalCount: 1,
      page: 1,
      pageSize: 50,
    });

    render(<AuditLogTab />);

    expect(await screen.findByText('agent.approve')).toBeInTheDocument();
    expect(screen.getByText('admin')).toBeInTheDocument();
    expect(screen.getByText('host-1')).toBeInTheDocument();
  });

  it('shows a placeholder when there are no entries', async () => {
    mockedGetPage.mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 50 });

    render(<AuditLogTab />);

    expect(await screen.findByText('No matching entries.')).toBeInTheDocument();
  });

  it('shows a dash for a null details field', async () => {
    mockedGetPage.mockResolvedValue({
      entries: [{ id: 1, timestamp: '2026-01-01T00:00:00Z', actor: 'admin', action: 'login.success', details: null }],
      totalCount: 1,
      page: 1,
      pageSize: 50,
    });

    render(<AuditLogTab />);

    expect(await screen.findByText('login.success')).toBeInTheDocument();
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('fetches page 1 with no search on first load', async () => {
    mockedGetPage.mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 50 });

    render(<AuditLogTab />);

    await screen.findByText('No matching entries.');
    expect(mockedGetPage).toHaveBeenCalledWith(1, 50, undefined);
  });

  it('searches and resets to page 1', async () => {
    mockedGetPage.mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 50 });
    const user = userEvent.setup();

    render(<AuditLogTab />);
    await screen.findByText('No matching entries.');

    await user.type(screen.getByLabelText('Search'), 'delete');
    await user.click(screen.getByRole('button', { name: 'Search' }));

    expect(mockedGetPage).toHaveBeenLastCalledWith(1, 50, 'delete');
  });

  it('searches when Enter is pressed in the search field', async () => {
    mockedGetPage.mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 50 });
    const user = userEvent.setup();

    render(<AuditLogTab />);
    await screen.findByText('No matching entries.');

    await user.type(screen.getByLabelText('Search'), 'delete{Enter}');

    expect(mockedGetPage).toHaveBeenLastCalledWith(1, 50, 'delete');
  });

  it('paginates with Previous/Next', async () => {
    mockedGetPage.mockResolvedValue({
      entries: [{ id: 1, timestamp: '2026-01-01T00:00:00Z', actor: 'admin', action: 'agent.approve', details: null }],
      totalCount: 120,
      page: 1,
      pageSize: 50,
    });
    const user = userEvent.setup();

    render(<AuditLogTab />);
    await screen.findByText('Page 1 of 3');

    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();

    mockedGetPage.mockResolvedValue({
      entries: [{ id: 2, timestamp: '2026-01-01T00:00:00Z', actor: 'admin', action: 'agent.delete', details: null }],
      totalCount: 120,
      page: 2,
      pageSize: 50,
    });
    await user.click(screen.getByRole('button', { name: 'Next' }));

    await screen.findByText('Page 2 of 3');
    expect(mockedGetPage).toHaveBeenLastCalledWith(2, 50, undefined);
    expect(screen.getByRole('button', { name: 'Previous' })).toBeEnabled();
  });

  it('shows an error message when the page request fails', async () => {
    mockedGetPage.mockRejectedValue(new Error('network error'));

    render(<AuditLogTab />);

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });
});
