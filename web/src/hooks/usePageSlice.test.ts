import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { usePageSlice } from './usePageSlice';

describe('usePageSlice', () => {
  it('slices the array into the requested page size', () => {
    const items = Array.from({ length: 25 }, (_, i) => i);
    const { result } = renderHook(() => usePageSlice(items, 10));

    expect(result.current.totalPages).toBe(3);
    expect(result.current.pageItems).toEqual(items.slice(0, 10));

    act(() => result.current.setPage(3));

    expect(result.current.pageItems).toEqual(items.slice(20, 30));
  });

  it('treats pageSize 0 as unlimited — everything on one page', () => {
    const items = Array.from({ length: 500 }, (_, i) => i);
    const { result } = renderHook(() => usePageSlice(items, 0));

    expect(result.current.totalPages).toBe(1);
    expect(result.current.pageItems).toEqual(items);
  });

  it('clamps back to the last valid page when the item count shrinks', () => {
    const { result, rerender } = renderHook(({ items, pageSize }) => usePageSlice(items, pageSize), {
      initialProps: { items: Array.from({ length: 25 }, (_, i) => i), pageSize: 10 },
    });

    act(() => result.current.setPage(3));
    expect(result.current.page).toBe(3);

    rerender({ items: Array.from({ length: 5 }, (_, i) => i), pageSize: 10 });

    expect(result.current.page).toBe(1);
    expect(result.current.totalPages).toBe(1);
    // Not just eventually correct — pageItems must reflect the clamped page
    // on this exact render, with no intervening render where it's a stale,
    // out-of-range (and therefore empty) slice.
    expect(result.current.pageItems).toEqual([0, 1, 2, 3, 4]);
  });

  it('returns at least page 1 for an empty array', () => {
    const { result } = renderHook(() => usePageSlice<number>([], 10));

    expect(result.current.totalPages).toBe(1);
    expect(result.current.pageItems).toEqual([]);
  });
});
