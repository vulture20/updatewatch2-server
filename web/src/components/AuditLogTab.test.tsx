import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { adminApi, auditLogApi } from '../api/endpoints';
import type { AdminSettings } from '../api/types';
import { AuditLogTab } from './AuditLogTab';

vi.mock('../api/endpoints', () => ({
  auditLogApi: {
    getPage: vi.fn(),
  },
  adminApi: {
    getSettings: vi.fn(),
  },
}));

const mockedGetPage = vi.mocked(auditLogApi.getPage);
const mockedGetSettings = vi.mocked(adminApi.getSettings);

describe('AuditLogTab', () => {
  beforeEach(() => {
    mockedGetPage.mockReset();
    mockedGetSettings.mockReset().mockResolvedValue({ auditLogItemsPerPage: 50 } as AdminSettings);
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
    expect(mockedGetPage).toHaveBeenCalledWith(1, 50, undefined, false);
  });

  it('searches and resets to page 1', async () => {
    mockedGetPage.mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 50 });
    const user = userEvent.setup();

    render(<AuditLogTab />);
    await screen.findByText('No matching entries.');

    await user.type(screen.getByLabelText('Search'), 'delete');
    await user.click(screen.getByRole('button', { name: 'Search' }));

    expect(mockedGetPage).toHaveBeenLastCalledWith(1, 50, 'delete', false);
  });

  it('searches when Enter is pressed in the search field', async () => {
    mockedGetPage.mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 50 });
    const user = userEvent.setup();

    render(<AuditLogTab />);
    await screen.findByText('No matching entries.');

    await user.type(screen.getByLabelText('Search'), 'delete{Enter}');

    expect(mockedGetPage).toHaveBeenLastCalledWith(1, 50, 'delete', false);
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
    expect(mockedGetPage).toHaveBeenLastCalledWith(2, 50, undefined, false);
    expect(screen.getByRole('button', { name: 'Previous' })).toBeEnabled();
  });

  it('shows an error message when the page request fails', async () => {
    mockedGetPage.mockRejectedValue(new Error('network error'));

    render(<AuditLogTab />);

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('translates the "unlimited" items-per-page setting (0) into the explicit unlimited flag', async () => {
    mockedGetSettings.mockReset().mockResolvedValue({ auditLogItemsPerPage: 0 } as AdminSettings);
    mockedGetPage.mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 0 });

    render(<AuditLogTab />);

    await screen.findByText('No matching entries.');
    expect(mockedGetPage).toHaveBeenCalledWith(1, 0, undefined, true);
  });

  it('uses the independent auditLogItemsPerPage setting, not the shared itemsPerPage one', async () => {
    mockedGetSettings.mockReset().mockResolvedValue({ itemsPerPage: 10, auditLogItemsPerPage: 100 } as AdminSettings);
    mockedGetPage.mockResolvedValue({ entries: [], totalCount: 0, page: 1, pageSize: 100 });

    render(<AuditLogTab />);

    await screen.findByText('No matching entries.');
    expect(mockedGetPage).toHaveBeenCalledWith(1, 100, undefined, false);
  });

  it('jumps directly to a page number', async () => {
    mockedGetPage.mockResolvedValue({
      entries: [{ id: 1, timestamp: '2026-01-01T00:00:00Z', actor: 'admin', action: 'agent.approve', details: null }],
      totalCount: 500,
      page: 1,
      pageSize: 50,
    });
    const user = userEvent.setup();

    render(<AuditLogTab />);
    await screen.findByText('Page 1 of 10');

    mockedGetPage.mockResolvedValue({
      entries: [{ id: 2, timestamp: '2026-01-01T00:00:00Z', actor: 'admin', action: 'agent.delete', details: null }],
      totalCount: 500,
      page: 10,
      pageSize: 50,
    });
    // With current=1/total=10, buildPageNumbers renders [1, 2, ellipsis, 10] —
    // page 10 (the last page) is always present regardless of current page.
    await user.click(screen.getByRole('button', { name: 'Page 10' }));

    await screen.findByText('Page 10 of 10');
    expect(mockedGetPage).toHaveBeenLastCalledWith(10, 50, undefined, false);
  });
});
