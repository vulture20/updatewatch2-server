import { useState } from 'react';

export interface PageSlice<T> {
  /** 1-based. */
  page: number;
  setPage: (page: number) => void;
  totalPages: number;
  /** The slice of `items` belonging to the current page (the whole array when `pageSize` is 0). */
  pageItems: T[];
}

/**
 * Client-side pagination of an already-loaded, already-filtered/sorted
 * array — used by AgentsListPage/SchedulesListPage, whose backing
 * endpoints stay fully unpaged (see the same user-confirmed decision
 * AuditLogTab's own server-side pagination does NOT follow, since that one
 * already paginates in the database). `pageSize === 0` means unlimited
 * (AdminSettings.ItemsPerPage's own sentinel) — everything renders on one
 * page.
 */
export function usePageSlice<T>(items: T[], pageSize: number): PageSlice<T> {
  const [page, setPage] = useState(1);

  const totalPages = pageSize === 0 ? 1 : Math.max(1, Math.ceil(items.length / pageSize));

  // Clamp back into range whenever the underlying list (a new filter/sort/
  // search) or the page size itself shrinks totalPages below the
  // currently-viewed page — otherwise a filter change could strand the
  // view on a now-empty out-of-range page. Adjusted directly during render
  // (React's own documented pattern for this, not a useEffect): a
  // post-render effect would still let THIS render compute `pageItems` from
  // the stale, now out-of-range `page` first — a real one-frame flash of an
  // empty page before the effect fires and corrects it. Calling `setPage`
  // here instead restarts the render immediately, before anything paints,
  // and `effectivePage` below covers the render that's currently in
  // progress too, so `pageItems` is never wrong even for that one frame.
  if (page > totalPages) {
    setPage(totalPages);
  }
  const effectivePage = Math.min(page, totalPages);

  const pageItems = pageSize === 0 ? items : items.slice((effectivePage - 1) * pageSize, effectivePage * pageSize);

  return { page: effectivePage, setPage, totalPages, pageItems };
}
