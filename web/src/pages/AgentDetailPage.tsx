import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { OneTimeSecretDialog } from '../components/OneTimeSecretDialog';
import { WarningTriangleIcon } from '../components/WarningTriangleIcon';
import { agentsApi } from '../api/endpoints';
import type { AgentDetail, UpdateItem } from '../api/types';
import { sortBy, toggleSort, type SortState } from '../utils/sorting';

// Same reasoning as AgentsListPage's own constant — approving an agent,
// then watching its certificate/updates actually arrive, shouldn't need a
// manual browser reload to see progress.
const POLL_INTERVAL_MS = 5000;

type UpdateSortKey = 'title' | 'pkg' | 'detected';

export function AgentDetailPage() {
  const { t } = useTranslation();
  const { hostname } = useParams<{ hostname: string }>();
  const navigate = useNavigate();
  const [agent, setAgent] = useState<AgentDetail | null>(null);
  const [updates, setUpdates] = useState<UpdateItem[]>([]);
  const [notFound, setNotFound] = useState(false);
  const [reissuedToken, setReissuedToken] = useState<string | null>(null);
  const [updatesSort, setUpdatesSort] = useState<SortState<UpdateSortKey>>({ key: null, dir: 'asc' });
  // Which updates an admin has explicitly unchecked, to install only some
  // while sparing others — tracked as the deselected set, not the
  // selected one, so a newly reported update defaults to selected/checked
  // (matching the previous "install everything" behavior) without this
  // page needing to notice it arrived. An update with no PackageId (rare;
  // see WuaUpdateSession's own doc comment) can't be individually named
  // on the wire, so it's always installed and never appears here.
  const [deselectedUpdateIds, setDeselectedUpdateIds] = useState<Set<number>>(new Set());
  // Ref, not state — see AgentsListPage's identical use for why: a
  // background poll failure must not replace an already-rendered agent
  // with the not-found state, only the very first load failing should.
  const hasLoadedOnceRef = useRef(false);

  const reload = () => {
    if (!hostname) {
      return;
    }
    agentsApi
      .get(hostname)
      .then((data) => {
        hasLoadedOnceRef.current = true;
        setAgent(data);
      })
      .catch(() => {
        if (!hasLoadedOnceRef.current) {
          setNotFound(true);
        }
      });
    agentsApi
      .updates(hostname)
      .then(setUpdates)
      .catch(() => {
        // covered by the agent-load error state above
      });
  };

  useEffect(() => {
    hasLoadedOnceRef.current = false;
    reload();
    const id = setInterval(reload, POLL_INTERVAL_MS);
    return () => clearInterval(id);
  }, [hostname]);

  const sortedUpdates = useMemo(
    () =>
      sortBy(updates, updatesSort, {
        title: (u) => u.title,
        pkg: (u) => u.packageId ?? '',
        detected: (u) => u.detectedAt,
      }),
    [updates, updatesSort],
  );

  const isForcedUpdate = (update: UpdateItem) => update.packageId === null;
  const isUpdateSelected = (update: UpdateItem) => isForcedUpdate(update) || !deselectedUpdateIds.has(update.id);
  const selectedUpdateIds = updates.filter(isUpdateSelected).map((u) => u.id);
  const deselectableUpdates = updates.filter((u) => !isForcedUpdate(u));
  const allSelected = deselectableUpdates.every((u) => !deselectedUpdateIds.has(u.id));

  const toggleUpdateSelected = (update: UpdateItem) => {
    if (isForcedUpdate(update)) {
      return;
    }
    setDeselectedUpdateIds((prev) => {
      const next = new Set(prev);
      if (next.has(update.id)) {
        next.delete(update.id);
      } else {
        next.add(update.id);
      }
      return next;
    });
  };

  const toggleSelectAll = () => {
    setDeselectedUpdateIds(allSelected ? new Set(deselectableUpdates.map((u) => u.id)) : new Set());
  };

  const triggerInstall = () => {
    if (!agent) {
      return;
    }
    void agentsApi.triggerInstall(agent.hostname, selectedUpdateIds).then(reload);
  };

  const sortArrow = (key: UpdateSortKey) => (updatesSort.key === key ? (updatesSort.dir === 'asc' ? '▲' : '▼') : '');
  const sortHeaderProps = (key: UpdateSortKey) => ({
    className: 'sortable-header',
    onClick: () => setUpdatesSort((prev) => toggleSort(prev, key)),
  });

  const triggerRestart = () => {
    if (!agent || !window.confirm(t('agentDetail.restartConfirm'))) {
      return;
    }
    void agentsApi.triggerRestart(agent.hostname).then(reload);
  };

  const reissueCertificate = () => {
    if (!agent || !window.confirm(t('agentDetail.reissueConfirm'))) {
      return;
    }
    void agentsApi.reissueCertificate(agent.hostname).then((result) => {
      setReissuedToken(result.registrationToken);
      reload();
    });
  };

  const deleteAgent = () => {
    if (!agent || !window.confirm(t('agentDetail.deleteConfirm', { hostname: agent.hostname }))) {
      return;
    }
    void agentsApi.delete(agent.hostname).then(() => navigate('/agents'));
  };

  if (notFound) {
    return <p role="alert">Agent not found.</p>;
  }

  if (!agent) {
    return <p>{t('agents.loading')}</p>;
  }

  return (
    <section>
      <div className="breadcrumb">
        <a
          href="/agents"
          onClick={(e) => {
            e.preventDefault();
            navigate('/agents');
          }}
        >
          {t('agents.title')}
        </a>
        <span className="text-muted">/</span>
        <span className="text-muted">{agent.hostname}</span>
      </div>

      <div className="detail-header">
        <div className="detail-header-title">
          <h1>{agent.hostname}</h1>
          <span className={agent.approved ? 'tag tag-accent' : 'tag tag-outline'}>
            {agent.approved ? t('agents.approved') : t('agents.filters.pending')}
          </span>
          {agent.rebootRequired && <span className="tag tag-neutral">{t('agents.rebootRequired')}</span>}
        </div>
        <div className="detail-header-actions">
          {!agent.approved && (
            <button type="button" className="btn-accent" onClick={() => void agentsApi.approve(agent.hostname).then(reload)}>
              {t('agentDetail.approve')}
            </button>
          )}
          {agent.approved && (
            <button type="button" onClick={reissueCertificate}>
              {t('agentDetail.reissueCertificate')}
            </button>
          )}
          {agent.approved && (
            <button type="button" disabled={Boolean(agent.pendingRestartRequestedAt)} onClick={triggerRestart}>
              {agent.pendingRestartRequestedAt ? t('agentDetail.restartPending') : t('agentDetail.triggerRestart')}
            </button>
          )}
          <button
            type="button"
            disabled={selectedUpdateIds.length === 0 || Boolean(agent.pendingInstallRequestedAt)}
            onClick={triggerInstall}
          >
            {agent.pendingInstallRequestedAt
              ? t('agentDetail.installPending')
              : t('agentDetail.triggerInstall', { count: selectedUpdateIds.length })}
          </button>
          <button type="button" onClick={deleteAgent}>
            {t('agentDetail.delete')}
          </button>
        </div>
      </div>

      {agent.lastCertificateRejectionReason && (
        <div role="alert" className="banner banner-accent">
          <WarningTriangleIcon />
          <span>
            {t('agentDetail.certificateRejectionReason')}:{' '}
            {t(`agentDetail.certificateRejectionReasons.${agent.lastCertificateRejectionReason}`)}
            {agent.lastCertificateRejectionAt && ` (${new Date(agent.lastCertificateRejectionAt).toLocaleString()})`}
          </span>
        </div>
      )}

      <div className="detail-cards">
        <div className="card">
          <span className="card-kicker">{t('agentDetail.cards.identity')}</span>
          <dl>
            <dt className="text-muted">{t('agentDetail.dnsName')}</dt>
            <dd>{agent.dnsName ?? '—'}</dd>
            <dt className="text-muted">{t('agentDetail.operatingSystem')}</dt>
            <dd>{agent.operatingSystem ?? '—'}</dd>
            <dt className="text-muted">{t('agentDetail.ipAddress')}</dt>
            <dd>{agent.ipAddress ?? '—'}</dd>
            <dt className="text-muted">{t('agentDetail.agentVersion')}</dt>
            <dd>{agent.agentVersion ?? '—'}</dd>
            <dt className="text-muted">{t('agentDetail.lastAliveAt')}</dt>
            <dd>{agent.lastAliveAt ? new Date(agent.lastAliveAt).toLocaleString() : t('agentDetail.never')}</dd>
          </dl>
        </div>

        <div className="card">
          <span className="card-kicker">{t('agentDetail.cards.certificate')}</span>
          <dl>
            <dt className="text-muted">{t('agentDetail.certificateThumbprintSha256')}</dt>
            <dd>{agent.clientCertificateThumbprint ?? '—'}</dd>
            <dt className="text-muted">{t('agentDetail.certificateThumbprintSha1')}</dt>
            <dd>{agent.clientCertificateThumbprintSha1 ?? '—'}</dd>
            <dt className="text-muted">{t('agentDetail.certificateIssuedAt')}</dt>
            <dd>{agent.clientCertificateIssuedAt ? new Date(agent.clientCertificateIssuedAt).toLocaleString() : '—'}</dd>
            <dt className="text-muted">{t('agentDetail.certificateExpiresAt')}</dt>
            <dd>{agent.clientCertificateExpiresAt ? new Date(agent.clientCertificateExpiresAt).toLocaleString() : '—'}</dd>
            <dt className="text-muted">{t('agentDetail.issuingCaRoot')}</dt>
            <dd>{agent.issuingRootThumbprint ?? '—'}</dd>
          </dl>
        </div>

        <div className="card">
          <span className="card-kicker">{t('agentDetail.cards.install')}</span>
          <dl>
            <dt className="text-muted">{t('agentDetail.lastInstallOutcome')}</dt>
            <dd>
              {agent.pendingInstallRequestedAt
                ? t('agentDetail.installPending')
                : agent.lastInstallOutcome
                  ? t(`agentDetail.installOutcome.${agent.lastInstallOutcome}`)
                  : '—'}
            </dd>
            <dt className="text-muted">{t('agentDetail.cards.installCompletedAt')}</dt>
            <dd>
              {agent.pendingInstallRequestedAt || !agent.lastInstallCompletedAt
                ? '—'
                : new Date(agent.lastInstallCompletedAt).toLocaleString()}
            </dd>
            {!agent.pendingInstallRequestedAt && agent.lastInstallOutcome === 'Failed' && agent.lastInstallErrorDetail && (
              <>
                <dt className="text-muted">{t('agentDetail.lastInstallErrorDetail')}</dt>
                <dd className="text-monospace">{agent.lastInstallErrorDetail}</dd>
              </>
            )}
          </dl>
        </div>

        <div className="card">
          <span className="card-kicker">{t('agentDetail.cards.restart')}</span>
          <dl>
            <dt className="text-muted">{t('agentDetail.lastRestartOutcome')}</dt>
            <dd>
              {agent.pendingRestartRequestedAt
                ? t('agentDetail.restartPending')
                : agent.lastRestartOutcome
                  ? t(`agentDetail.restartOutcome.${agent.lastRestartOutcome}`)
                  : '—'}
            </dd>
            <dt className="text-muted">{t('agentDetail.cards.restartCompletedAt')}</dt>
            <dd>
              {agent.pendingRestartRequestedAt || !agent.lastRestartCompletedAt
                ? '—'
                : new Date(agent.lastRestartCompletedAt).toLocaleString()}
            </dd>
            {!agent.pendingRestartRequestedAt && agent.lastRestartOutcome === 'Failed' && agent.lastRestartErrorDetail && (
              <>
                <dt className="text-muted">{t('agentDetail.lastRestartErrorDetail')}</dt>
                <dd className="text-monospace">{agent.lastRestartErrorDetail}</dd>
              </>
            )}
          </dl>
        </div>
      </div>

      {reissuedToken && (
        <OneTimeSecretDialog
          label={t('agentDetail.reissueTokenTitle')}
          body={t('agentDetail.reissueTokenBody')}
          value={reissuedToken}
          copyLabel={t('agentDetail.copyToken')}
          copiedLabel={t('agentDetail.copied')}
          closeLabel={t('agentDetail.close')}
          onClose={() => setReissuedToken(null)}
        />
      )}

      <h2 style={{ fontSize: '18px', margin: '22px 0 10px' }}>{t('agentDetail.updates')}</h2>
      {updates.length === 0 ? (
        <p>{t('agentDetail.noUpdates')}</p>
      ) : (
        <div className="card table-card">
          <table>
            <thead>
              <tr>
                <th>
                  <input
                    type="checkbox"
                    checked={allSelected}
                    disabled={deselectableUpdates.length === 0}
                    onChange={toggleSelectAll}
                    aria-label={t('agentDetail.selectAllUpdates')}
                  />
                </th>
                <th>
                  <button type="button" {...sortHeaderProps('title')}>
                    {t('agentDetail.updateTitle')} <span className="sort-arrow">{sortArrow('title')}</span>
                  </button>
                </th>
                <th>
                  <button type="button" {...sortHeaderProps('pkg')}>
                    {t('agentDetail.updatePackageId')} <span className="sort-arrow">{sortArrow('pkg')}</span>
                  </button>
                </th>
                <th>
                  <button type="button" {...sortHeaderProps('detected')}>
                    {t('agentDetail.updateDetectedAt')} <span className="sort-arrow">{sortArrow('detected')}</span>
                  </button>
                </th>
              </tr>
            </thead>
            <tbody>
              {sortedUpdates.map((update) => (
                <tr key={update.id}>
                  <td>
                    <input
                      type="checkbox"
                      checked={isUpdateSelected(update)}
                      disabled={isForcedUpdate(update)}
                      onChange={() => toggleUpdateSelected(update)}
                      title={isForcedUpdate(update) ? t('agentDetail.forcedUpdateHint') : undefined}
                      aria-label={t('agentDetail.selectUpdate', { title: update.title })}
                    />
                  </td>
                  <td>{update.title}</td>
                  <td className="text-muted">{update.packageId ?? '—'}</td>
                  <td className="text-muted">{new Date(update.detectedAt).toLocaleDateString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
