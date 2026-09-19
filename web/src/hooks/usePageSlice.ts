import { useEffect, useState } from 'react';

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
  // view on a now-empty out-of-range page.
  useEffect(() => {
    if (page > totalPages) {
      setPage(totalPages);
    }
  }, [page, totalPages]);

  const pageItems = pageSize === 0 ? items : items.slice((page - 1) * pageSize, page * pageSize);

  return { page, setPage, totalPages, pageItems };
}
