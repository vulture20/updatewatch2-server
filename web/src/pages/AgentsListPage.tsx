import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { agentsApi } from '../api/endpoints';
import type { AgentListItem } from '../api/types';

// How often this page re-fetches on its own, so an approval (or a
// server-side state change like a certificate finally arriving) shows up
// without the admin needing a manual browser reload.
const POLL_INTERVAL_MS = 5000;

export function AgentsListPage() {
  const { t } = useTranslation();
  const [agents, setAgents] = useState<AgentListItem[] | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [error, setError] = useState(false);
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
          <button type="button" className="btn-accent" disabled={selected.size === 0} onClick={() => void approveSelected()}>
            {t('agents.approveSelected')} ({selected.size})
          </button>
          <table>
            <thead>
              <tr>
                <th aria-label="select" />
                <th>{t('agents.hostname')}</th>
                <th>{t('agents.approved')}</th>
                <th>{t('agents.rebootRequired')}</th>
                <th>{t('agents.pendingUpdates')}</th>
              </tr>
            </thead>
            <tbody>
              {agents.map((agent) => (
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
                    <Link to={`/agents/${encodeURIComponent(agent.hostname)}`}>{agent.hostname}</Link>
                  </td>
                  <td>{agent.approved ? t('agents.yes') : t('agents.no')}</td>
                  <td>{agent.rebootRequired ? t('agents.yes') : t('agents.no')}</td>
                  <td>{agent.pendingUpdateCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </section>
  );
}
