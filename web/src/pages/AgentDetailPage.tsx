import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { OneTimeSecretDialog } from '../components/OneTimeSecretDialog';
import { agentsApi } from '../api/endpoints';
import type { AgentDetail, UpdateItem } from '../api/types';

export function AgentDetailPage() {
  const { t } = useTranslation();
  const { hostname } = useParams<{ hostname: string }>();
  const [agent, setAgent] = useState<AgentDetail | null>(null);
  const [updates, setUpdates] = useState<UpdateItem[]>([]);
  const [notFound, setNotFound] = useState(false);
  const [reissuedToken, setReissuedToken] = useState<string | null>(null);

  const reload = () => {
    if (!hostname) {
      return;
    }
    agentsApi
      .get(hostname)
      .then(setAgent)
      .catch(() => setNotFound(true));
    agentsApi
      .updates(hostname)
      .then(setUpdates)
      .catch(() => {
        // covered by the agent-load error state above
      });
  };

  useEffect(reload, [hostname]);

  const reissueCertificate = () => {
    if (!agent || !window.confirm(t('agentDetail.reissueConfirm'))) {
      return;
    }
    void agentsApi.reissueCertificate(agent.hostname).then((result) => {
      setReissuedToken(result.registrationToken);
      reload();
    });
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
        <dt>{t('agentDetail.certificateThumbprint')}</dt>
        <dd>{agent.clientCertificateThumbprint ?? '—'}</dd>
        <dt>{t('agentDetail.certificateIssuedAt')}</dt>
        <dd>{agent.clientCertificateIssuedAt ? new Date(agent.clientCertificateIssuedAt).toLocaleString() : '—'}</dd>
        <dt>{t('agentDetail.certificateExpiresAt')}</dt>
        <dd>{agent.clientCertificateExpiresAt ? new Date(agent.clientCertificateExpiresAt).toLocaleString() : '—'}</dd>
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
        disabled={updates.length === 0}
        onClick={() => void agentsApi.triggerInstall(agent.hostname)}
      >
        {t('agentDetail.triggerInstall')}
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
