import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { OneTimeSecretDialog } from '../components/OneTimeSecretDialog';
import { agentsApi } from '../api/endpoints';
import type { AgentDetail, UpdateItem } from '../api/types';

// Same reasoning as AgentsListPage's own constant — approving an agent,
// then watching its certificate/updates actually arrive, shouldn't need a
// manual browser reload to see progress.
const POLL_INTERVAL_MS = 5000;

export function AgentDetailPage() {
  const { t } = useTranslation();
  const { hostname } = useParams<{ hostname: string }>();
  const navigate = useNavigate();
  const [agent, setAgent] = useState<AgentDetail | null>(null);
  const [updates, setUpdates] = useState<UpdateItem[]>([]);
  const [notFound, setNotFound] = useState(false);
  const [reissuedToken, setReissuedToken] = useState<string | null>(null);
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
      <h1>{agent.hostname}</h1>
      <dl>
        <dt>{t('agentDetail.dnsName')}</dt>
        <dd>{agent.dnsName ?? '—'}</dd>
        <dt>{t('agentDetail.operatingSystem')}</dt>
        <dd>{agent.operatingSystem ?? '—'}</dd>
        <dt>{t('agentDetail.ipAddress')}</dt>
        <dd>{agent.ipAddress ?? '—'}</dd>
        <dt>{t('agentDetail.agentVersion')}</dt>
        <dd>{agent.agentVersion ?? '—'}</dd>
        <dt>{t('agentDetail.lastAliveAt')}</dt>
        <dd>{agent.lastAliveAt ? new Date(agent.lastAliveAt).toLocaleString() : t('agentDetail.never')}</dd>
        <dt>{t('agentDetail.certificateThumbprintSha256')}</dt>
        <dd>{agent.clientCertificateThumbprint ?? '—'}</dd>
        <dt>{t('agentDetail.certificateThumbprintSha1')}</dt>
        <dd>{agent.clientCertificateThumbprintSha1 ?? '—'}</dd>
        <dt>{t('agentDetail.certificateIssuedAt')}</dt>
        <dd>{agent.clientCertificateIssuedAt ? new Date(agent.clientCertificateIssuedAt).toLocaleString() : '—'}</dd>
        <dt>{t('agentDetail.certificateExpiresAt')}</dt>
        <dd>{agent.clientCertificateExpiresAt ? new Date(agent.clientCertificateExpiresAt).toLocaleString() : '—'}</dd>
        <dt>{t('agentDetail.lastInstallOutcome')}</dt>
        <dd>
          {agent.pendingInstallRequestedAt
            ? t('agentDetail.installPending')
            : agent.lastInstallOutcome
              ? `${t(`agentDetail.installOutcome.${agent.lastInstallOutcome}`)} (${new Date(agent.lastInstallCompletedAt!).toLocaleString()})`
              : '—'}
        </dd>
      </dl>

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
      </button>{' '}

      <button type="button" className="btn-danger" onClick={deleteAgent}>
        {t('agentDetail.delete')}
      </button>

      <h2>{t('agentDetail.updates')}</h2>
      {updates.length === 0 ? (
        <p>{t('agentDetail.noUpdates')}</p>
      ) : (
        <ul>
          {updates.map((update) => (
            <li key={update.id}>
              {update.title}
              {update.packageId ? ` (${update.packageId})` : ''}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
