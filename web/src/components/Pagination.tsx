import { useTranslation } from 'react-i18next';

type PageEntry = number | 'ellipsis-before' | 'ellipsis-after';

/**
 * Builds the list of page numbers to render, always including page 1 and
 * `total`, plus a window of `current - 1 .. current + 1`, collapsing any
 * gap into a single ellipsis marker. Exported separately so its truncation
 * logic has direct unit coverage without needing to render the component.
 */
export function buildPageNumbers(current: number, total: number): PageEntry[] {
  if (total <= 1) {
    return total === 1 ? [1] : [];
  }

  let windowStart = Math.max(2, current - 1);
  let windowEnd = Math.min(total - 1, current + 1);

  // A "gap" of exactly one hidden page number isn't worth collapsing into
  // an ellipsis — showing that one page directly is no wider than the
  // ellipsis it would otherwise become, and reads better than a "…"
  // standing in for a single number (e.g. total=4 should render 1 2 3 4,
  // never 1 2 … 4).
  if (windowStart === 3) {
    windowStart = 2;
  }
  if (windowEnd === total - 2) {
    windowEnd = total - 1;
  }

  const entries: PageEntry[] = [1];

  if (windowStart > 2) {
    entries.push('ellipsis-before');
  }

  for (let page = windowStart; page <= windowEnd; page += 1) {
    entries.push(page);
  }

  if (windowEnd < total - 1) {
    entries.push('ellipsis-after');
  }

  entries.push(total);

  return entries;
}

interface PaginationProps {
  /** 1-based current page. */
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
}

/**
 * Shared Previous/page-number/Next pager, used by AuditLogTab (the
 * original, server-paginated reference implementation), AgentsListPage,
 * and SchedulesListPage (both client-side pagination of an already-loaded
 * list) — at the user's explicit request to add page-number jump buttons
 * everywhere pagination exists, not just Previous/Next.
 */
export function Pagination({ page, totalPages, onPageChange }: PaginationProps) {
  const { t } = useTranslation();

  return (
    <div className="pagination">
      <button type="button" className="btn-ghost" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
        {t('pagination.previous')}
      </button>
      <div className="pagination-pages">
        {buildPageNumbers(page, totalPages).map((entry) =>
          entry === 'ellipsis-before' || entry === 'ellipsis-after' ? (
            <span key={entry} aria-hidden="true" className="text-muted">
              …
            </span>
          ) : (
            <button
              key={entry}
              type="button"
              className={entry === page ? 'btn-accent' : 'btn-ghost'}
              disabled={entry === page}
              aria-current={entry === page ? 'page' : undefined}
              aria-label={t('pagination.page', { page: entry })}
              onClick={() => onPageChange(entry)}
            >
              {entry}
            </button>
          ),
        )}
      </div>
      <span className="text-muted">{t('pagination.pageIndicator', { page, totalPages })}</span>
      <button type="button" className="btn-ghost" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
        {t('pagination.next')}
      </button>
    </div>
  );
}
