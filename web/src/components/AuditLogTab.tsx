import { useEffect, useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { auditLogApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { Pagination } from './Pagination';
import { useItemsPerPageState } from '../hooks/useItemsPerPage';
import type { AuditLogPage } from '../api/types';

/**
 * Administration → Audit Log — a read-only, paginated view of every
 * administrative/security-relevant action (CLAUDE.md's audit-log
 * requirement), previously written but never actually shown anywhere in
 * the admin UI. A plain search box filters actor/action/details
 * server-side (AuditLogController) rather than only the current page, so
 * it can find an old entry without paging back to it by hand.
 */
export function AuditLogTab() {
  const { t, i18n } = useTranslation();
  const { itemsPerPage, loaded: itemsPerPageLoaded } = useItemsPerPageState('auditLog');
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState('');
  const [appliedSearch, setAppliedSearch] = useState('');
  const [data, setData] = useState<AuditLogPage | null>(null);
  const [error, setError] = useState<string | null>(null);

  // itemsPerPage changes live (Settings tab stays mounted alongside this
  // one, see AdminPage's `hidden`-tab pattern) — reset back to page 1
  // whenever it does, otherwise a since-out-of-range page keeps being
  // fetched (an empty response with no <Pagination> rendered to page back
  // out of, since that's only shown for a non-empty result). Adjusting
  // state directly during render (not in a useEffect) so the corrected
  // page is what the fetch effect below ever sees — an effect-based reset
  // would still fire one stale fetch with the old, now out-of-range page
  // first.
  const [prevItemsPerPage, setPrevItemsPerPage] = useState(itemsPerPage);
  if (itemsPerPage !== prevItemsPerPage) {
    setPrevItemsPerPage(itemsPerPage);
    setPage(1);
  }

  useEffect(() => {
    // Wait for the real itemsPerPage setting before ever fetching — firing
    // once with the hook's own placeholder default and again moments later
    // with the real value would mean two real HTTP requests, not just a
    // cosmetic flash (see useItemsPerPageState's own doc comment).
    if (!itemsPerPageLoaded) {
      return;
    }
    setError(null);
    // itemsPerPage === 0 is AdminSettings.ItemsPerPage's own "unlimited"
    // sentinel — translated here into the audit-log endpoint's own explicit
    // `unlimited` flag (see AuditLogController).
    auditLogApi
      .getPage(page, itemsPerPage, appliedSearch || undefined, itemsPerPage === 0)
      .then(setData)
      .catch((err) => setError(err instanceof ApiError ? err.message : t('login.genericError')));
  }, [page, appliedSearch, itemsPerPage, itemsPerPageLoaded, t]);

  const submitSearch = () => {
    setPage(1);
    setAppliedSearch(searchInput.trim());
  };

  // Not a nested <form> — this tab's content lives inside AdminPage's own
  // outer settings <form>, and a <form> inside a <form> is invalid HTML
  // whose submit behavior isn't reliable across browsers, so Enter-to-
  // search is wired by hand instead.
  const onSearchKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      submitSearch();
    }
  };

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <div className="card table-card">
      <div className="audit-log-header">
        <span className="card-kicker">{t('auditLog.title')}</span>
        <label>
          {t('auditLog.search')}
          <input type="text" value={searchInput} onChange={(e) => setSearchInput(e.target.value)} onKeyDown={onSearchKeyDown} />
        </label>
        <button type="button" onClick={submitSearch}>
          {t('auditLog.searchButton')}
        </button>
      </div>

      {error && <div role="alert" className="login-error">{error}</div>}

      {!itemsPerPageLoaded && !error && <p>{t('auditLog.loading')}</p>}

      {data && data.entries.length === 0 && <p>{t('auditLog.empty')}</p>}

      {data && data.entries.length > 0 && (
        <>
          <table>
            <thead>
              <tr>
                <th>{t('auditLog.timestamp')}</th>
                <th>{t('auditLog.actor')}</th>
                <th>{t('auditLog.action')}</th>
                <th>{t('auditLog.details')}</th>
              </tr>
            </thead>
            <tbody>
              {data.entries.map((entry) => (
                <tr key={entry.id}>
                  <td className="text-muted">{new Date(entry.timestamp).toLocaleString(i18n.language)}</td>
                  <td>{entry.actor}</td>
                  <td>{entry.action}</td>
                  <td className="text-muted">{entry.details ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <Pagination page={page} totalPages={totalPages} onPageChange={setPage} />
        </>
      )}
    </div>
  );
}
