import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { agentsApi } from '../api/endpoints';
import type { AgentListItem } from '../api/types';
import { OsIcon } from '../components/OsIcon';
import { WarningTriangleIcon } from '../components/WarningTriangleIcon';
import { formatRelativeTime } from '../utils/relativeTime';
import { sortBy, toggleSort, type SortState } from '../utils/sorting';

// How often this page re-fetches on its own, so an approval (or a
// server-side state change like a certificate finally arriving) shows up
// without the admin needing a manual browser reload.
const POLL_INTERVAL_MS = 5000;

type SortKey = 'hostname' | 'os' | 'status' | 'reboot' | 'pending' | 'lastSeen';
type StatFilter = 'pendingApproval' | 'reboot' | 'pendingUpdates' | null;

interface Filters {
  osType: 'all' | 'windows' | 'linux';
  os: string;
  status: 'all' | 'approved' | 'pending';
  reboot: 'all' | 'yes' | 'no';
  updates: 'all' | 'yes' | 'no';
  warning: 'all' | 'yes' | 'no';
}

const DEFAULT_FILTERS: Filters = { osType: 'all', os: 'all', status: 'all', reboot: 'all', updates: 'all', warning: 'all' };

export function AgentsListPage() {
  const { t, i18n } = useTranslation();
  const [agents, setAgents] = useState<AgentListItem[] | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [error, setError] = useState(false);
  const [sort, setSort] = useState<SortState<SortKey>>({ key: null, dir: 'asc' });
  const [statFilter, setStatFilter] = useState<StatFilter>(null);
  const [filters, setFilters] = useState<Filters>(DEFAULT_FILTERS);
  // Ref, not state: a background poll failure must not blow away an
  // already-rendered list — only the very first load failing should show
  // the hard error state. A ref survives across reload()'s closures
  // without needing reload itself to be re-created (and the interval
  // re-armed) every time agents/error change.
  const hasLoadedOnceRef = useRef(false);

  const reload = () => {
    agentsApi
      .list()
      .then((data) => {
        hasLoadedOnceRef.current = true;
        setAgents(data);
      })
      .catch(() => {
        if (!hasLoadedOnceRef.current) {
          setError(true);
        }
      });
  };

  useEffect(() => {
    reload();
    const id = setInterval(reload, POLL_INTERVAL_MS);
    return () => clearInterval(id);
  }, []);

  const toggle = (hostname: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(hostname)) {
        next.delete(hostname);
      } else {
        next.add(hostname);
      }
      return next;
    });
  };

  const approveSelected = async () => {
    await agentsApi.approveMany([...selected]);
    setSelected(new Set());
    reload();
  };

  const clearFilters = () => {
    setStatFilter(null);
    setFilters(DEFAULT_FILTERS);
  };

  const toggleStatFilter = (key: Exclude<StatFilter, null>) => setStatFilter((prev) => (prev === key ? null : key));

  const stats = useMemo(() => {
    const list = agents ?? [];
    return {
      total: list.length,
      pendingApproval: list.filter((a) => !a.approved).length,
      rebootRequired: list.filter((a) => a.rebootRequired).length,
      totalPending: list.reduce((sum, a) => sum + a.pendingUpdateCount, 0),
    };
  }, [agents]);

  const osOptions = useMemo(
    () => [...new Set((agents ?? []).map((a) => a.operatingSystem).filter((os): os is string => os !== null))].sort(),
    [agents],
  );

  const filteredAndSorted = useMemo(() => {
    const isWindows = (a: AgentListItem) => a.operatingSystem?.includes('Windows') ?? false;
    const filtered = (agents ?? []).filter((a) => {
      if (filters.osType === 'windows' && !isWindows(a)) return false;
      if (filters.osType === 'linux' && (isWindows(a) || !a.operatingSystem)) return false;
      if (filters.os !== 'all' && a.operatingSystem !== filters.os) return false;
      if (filters.status === 'approved' && !a.approved) return false;
      if (filters.status === 'pending' && a.approved) return false;
      if (filters.reboot === 'yes' && !a.rebootRequired) return false;
      if (filters.reboot === 'no' && a.rebootRequired) return false;
      if (filters.updates === 'yes' && a.pendingUpdateCount === 0) return false;
      if (filters.updates === 'no' && a.pendingUpdateCount > 0) return false;
      if (filters.warning === 'yes' && !a.lastCertificateRejectionReason) return false;
      if (filters.warning === 'no' && a.lastCertificateRejectionReason) return false;
      if (statFilter === 'pendingApproval' && a.approved) return false;
      if (statFilter === 'reboot' && !a.rebootRequired) return false;
      if (statFilter === 'pendingUpdates' && a.pendingUpdateCount === 0) return false;
      return true;
    });
    return sortBy(filtered, sort, {
      hostname: (a) => a.hostname,
      os: (a) => a.operatingSystem ?? '',
      status: (a) => (a.approved ? 1 : 0),
      reboot: (a) => (a.rebootRequired ? 1 : 0),
      pending: (a) => a.pendingUpdateCount,
      lastSeen: (a) => a.lastAliveAt ?? '',
    });
  }, [agents, filters, statFilter, sort]);

  const hasActiveFilters =
    statFilter !== null || Object.entries(filters).some(([key, value]) => value !== DEFAULT_FILTERS[key as keyof Filters]);

  const sortArrow = (key: SortKey) => (sort.key === key ? (sort.dir === 'asc' ? '▲' : '▼') : '');
  const sortHeaderProps = (key: SortKey) => ({
    className: 'sortable-header',
    onClick: () => setSort((prev) => toggleSort(prev, key)),
  });

  if (error) {
    return <p role="alert">Failed to load agents.</p>;
  }

  if (agents === null) {
    return <p>{t('agents.loading')}</p>;
  }

  return (
    <section>
      <h1>{t('agents.title')}</h1>

      {agents.length === 0 ? (
        <p>{t('agents.empty')}</p>
      ) : (
        <>
          <div className="stat-cards">
            <button
              type="button"
              className={statFilter === null ? 'card stat-card stat-card-active' : 'card stat-card'}
              onClick={() => setStatFilter(null)}
            >
              <span className="card-kicker">{t('agents.stats.total')}</span>
              <span className="card-title">{stats.total}</span>
            </button>
            <button
              type="button"
              className={statFilter === 'pendingApproval' ? 'card stat-card stat-card-active' : 'card stat-card'}
              onClick={() => toggleStatFilter('pendingApproval')}
            >
              <span className="card-kicker">{t('agents.stats.pendingApproval')}</span>
              <span className="card-title" style={stats.pendingApproval > 0 ? { color: 'var(--color-accent)' } : undefined}>
                {stats.pendingApproval}
              </span>
            </button>
            <button
              type="button"
              className={statFilter === 'reboot' ? 'card stat-card stat-card-active' : 'card stat-card'}
              onClick={() => toggleStatFilter('reboot')}
            >
              <span className="card-kicker">{t('agents.stats.rebootRequired')}</span>
              <span className="card-title">{stats.rebootRequired}</span>
            </button>
            <button
              type="button"
              className={statFilter === 'pendingUpdates' ? 'card stat-card stat-card-active' : 'card stat-card'}
              onClick={() => toggleStatFilter('pendingUpdates')}
            >
              <span className="card-kicker">{t('agents.stats.totalPending')}</span>
              <span className="card-title">{stats.totalPending}</span>
            </button>
          </div>

          <div className="card filter-card">
            <label>
              {t('agents.filters.osType')}
              <select value={filters.osType} onChange={(e) => setFilters({ ...filters, osType: e.target.value as Filters['osType'] })}>
                <option value="all">{t('agents.filters.all')}</option>
                <option value="windows">Windows</option>
                <option value="linux">Linux</option>
              </select>
            </label>
            <label>
              {t('agentDetail.operatingSystem')}
              <select value={filters.os} onChange={(e) => setFilters({ ...filters, os: e.target.value })}>
                <option value="all">{t('agents.filters.all')}</option>
                {osOptions.map((os) => (
                  <option key={os} value={os}>
                    {os}
                  </option>
                ))}
              </select>
            </label>
            <label>
              {t('agents.filters.status')}
              <select value={filters.status} onChange={(e) => setFilters({ ...filters, status: e.target.value as Filters['status'] })}>
                <option value="all">{t('agents.filters.all')}</option>
                <option value="approved">{t('agents.approved')}</option>
                <option value="pending">{t('agents.filters.pending')}</option>
              </select>
            </label>
            <label>
              {t('agents.rebootRequired')}
              <select value={filters.reboot} onChange={(e) => setFilters({ ...filters, reboot: e.target.value as Filters['reboot'] })}>
                <option value="all">{t('agents.filters.all')}</option>
                <option value="yes">{t('agents.yes')}</option>
                <option value="no">{t('agents.no')}</option>
              </select>
            </label>
            <label>
              {t('agents.pendingUpdates')}
              <select value={filters.updates} onChange={(e) => setFilters({ ...filters, updates: e.target.value as Filters['updates'] })}>
                <option value="all">{t('agents.filters.all')}</option>
                <option value="yes">{t('agents.filters.present')}</option>
                <option value="no">{t('agents.filters.none')}</option>
              </select>
            </label>
            <label>
              {t('agents.filters.warning')}
              <select value={filters.warning} onChange={(e) => setFilters({ ...filters, warning: e.target.value as Filters['warning'] })}>
                <option value="all">{t('agents.filters.all')}</option>
                <option value="yes">{t('agents.filters.warningPresent')}</option>
                <option value="no">{t('agents.filters.none')}</option>
              </select>
            </label>
            {hasActiveFilters && (
              <button type="button" className="btn-ghost" onClick={clearFilters}>
                {t('agents.filters.clear')}
              </button>
            )}
          </div>

          <div className="list-toolbar">
            <span className="text-muted">{t('agents.filteredCount', { filtered: filteredAndSorted.length, total: agents.length })}</span>
            <button type="button" className="btn-accent" disabled={selected.size === 0} onClick={() => void approveSelected()}>
              {t('agents.approveSelected')} ({selected.size})
            </button>
          </div>

          <div className="card table-card">
            <table>
              <thead>
                <tr>
                  <th aria-label="select" />
                  <th>
                    <button type="button" {...sortHeaderProps('hostname')}>
                      {t('agents.hostname')} <span className="sort-arrow">{sortArrow('hostname')}</span>
                    </button>
                  </th>
                  <th>
                    <button type="button" {...sortHeaderProps('os')}>
                      {t('agentDetail.operatingSystem')} <span className="sort-arrow">{sortArrow('os')}</span>
                    </button>
                  </th>
                  <th>
                    <button type="button" {...sortHeaderProps('status')}>
                      {t('agents.status')} <span className="sort-arrow">{sortArrow('status')}</span>
                    </button>
                  </th>
                  <th>
                    <button type="button" {...sortHeaderProps('reboot')}>
                      {t('agents.rebootRequired')} <span className="sort-arrow">{sortArrow('reboot')}</span>
                    </button>
                  </th>
                  <th>
                    <button type="button" {...sortHeaderProps('pending')}>
                      {t('agents.pendingUpdates')} <span className="sort-arrow">{sortArrow('pending')}</span>
                    </button>
                  </th>
                  <th>
                    <button type="button" {...sortHeaderProps('lastSeen')}>
                      {t('agents.lastSeen')} <span className="sort-arrow">{sortArrow('lastSeen')}</span>
                    </button>
                  </th>
                </tr>
              </thead>
              <tbody>
                {filteredAndSorted.map((agent) => (
                  <tr key={agent.hostname}>
                    <td>
                      <input
                        type="checkbox"
                        checked={selected.has(agent.hostname)}
                        onChange={() => toggle(agent.hostname)}
                        aria-label={`select ${agent.hostname}`}
                      />
                    </td>
                    <td>
                      {agent.lastCertificateRejectionReason && (
                        <>
                          <WarningTriangleIcon
                            title={t('agents.certificateRejectedIcon', { reason: agent.lastCertificateRejectionReason })}
                          />{' '}
                        </>
                      )}
                      <Link to={`/agents/${encodeURIComponent(agent.hostname)}`}>{agent.hostname}</Link>
                    </td>
                    <td className="os-cell">
                      <OsIcon operatingSystem={agent.operatingSystem} /> {agent.operatingSystem ?? '—'}
                    </td>
                    <td>
                      <span className={agent.approved ? 'tag tag-accent' : 'tag tag-outline'}>
                        {agent.approved ? t('agents.approved') : t('agents.filters.pending')}
                      </span>
                    </td>
                    <td>
                      <span className="tag tag-neutral">{agent.rebootRequired ? t('agents.yes') : '—'}</span>
                    </td>
                    <td>{agent.pendingUpdateCount}</td>
                    <td className="text-muted">{formatRelativeTime(agent.lastAliveAt, i18n.language) ?? t('agentDetail.never')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  );
}
