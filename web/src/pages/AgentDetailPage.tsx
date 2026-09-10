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

  const sortArrow = (key: UpdateSortKey) => (updatesSort.key === key ? (updatesSort.dir === 'asc' ? '▲' : '▼') : '');
  const sortHeaderProps = (key: UpdateSortKey) => ({
    className: 'sortable-header',
    onClick: () => setUpdatesSort((prev) => toggleSort(prev, key)),
  });

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
          <button
            type="button"
            disabled={updates.length === 0 || Boolean(agent.pendingInstallRequestedAt)}
            onClick={() => void agentsApi.triggerInstall(agent.hostname).then(reload)}
          >
            {agent.pendingInstallRequestedAt ? t('agentDetail.installPending') : t('agentDetail.triggerInstall')}
          </button>
          <button type="button" className="btn-danger" onClick={deleteAgent}>
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

      <h2>{t('agentDetail.updates')}</h2>
      {updates.length === 0 ? (
        <p>{t('agentDetail.noUpdates')}</p>
      ) : (
        <div className="card table-card">
          <table>
            <thead>
              <tr>
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
