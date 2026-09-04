import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { agentsApi } from '../api/endpoints';
import type { AgentListItem } from '../api/types';

export function AgentsListPage() {
  const { t } = useTranslation();
  const [agents, setAgents] = useState<AgentListItem[] | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [error, setError] = useState(false);

  const reload = () => {
    agentsApi
      .list()
      .then(setAgents)
      .catch(() => setError(true));
  };

  useEffect(reload, []);

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
