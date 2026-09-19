import { renderHook, waitFor } from '@testing-library/react';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { announceAdminSettingsSaved } from '../adminSettingsEvents';
import { adminApi } from '../api/endpoints';
import type { AdminSettings } from '../api/types';
import { useItemsPerPage } from './useItemsPerPage';

vi.mock('../api/endpoints', () => ({
  adminApi: {
    getSettings: vi.fn(),
  },
}));

const mockedGetSettings = vi.mocked(adminApi.getSettings);

describe('useItemsPerPage', () => {
  beforeEach(() => {
    mockedGetSettings.mockReset();
  });

  it('returns the default (50) before the settings fetch resolves', () => {
    mockedGetSettings.mockReturnValue(new Promise(() => {})); // never resolves
    const { result } = renderHook(() => useItemsPerPage());

    expect(result.current).toBe(50);
  });

  it('updates to the real value once the settings fetch resolves', async () => {
    mockedGetSettings.mockResolvedValue({ itemsPerPage: 100 } as AdminSettings);
    const { result } = renderHook(() => useItemsPerPage());

    await waitFor(() => expect(result.current).toBe(100));
  });

  it('keeps the default when the settings fetch fails', async () => {
    mockedGetSettings.mockRejectedValue(new Error('no session'));
    const { result } = renderHook(() => useItemsPerPage());

    // Let the rejected promise's .catch() run.
    await act(async () => {
      await Promise.resolve();
    });

    expect(result.current).toBe(50);
  });

  it('updates live when a settings save is announced', async () => {
    mockedGetSettings.mockResolvedValue({ itemsPerPage: 50 } as AdminSettings);
    const { result } = renderHook(() => useItemsPerPage());
    await waitFor(() => expect(result.current).toBe(50));

    act(() => announceAdminSettingsSaved({ smtpConfigured: true, itemsPerPage: 200 }));

    expect(result.current).toBe(200);
  });
});
