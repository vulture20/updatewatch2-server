import { useEffect, useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { auditLogApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import type { AuditLogPage } from '../api/types';

const PAGE_SIZE = 50;

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
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState('');
  const [appliedSearch, setAppliedSearch] = useState('');
  const [data, setData] = useState<AuditLogPage | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setError(null);
    auditLogApi
      .getPage(page, PAGE_SIZE, appliedSearch || undefined)
      .then(setData)
      .catch((err) => setError(err instanceof ApiError ? err.message : t('login.genericError')));
  }, [page, appliedSearch, t]);

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

          <div className="audit-log-pagination">
            <button type="button" className="btn-ghost" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
              {t('auditLog.previousPage')}
            </button>
            <span className="text-muted">{t('auditLog.pageIndicator', { page, totalPages })}</span>
            <button type="button" className="btn-ghost" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
              {t('auditLog.nextPage')}
            </button>
          </div>
        </>
      )}
    </div>
  );
}
