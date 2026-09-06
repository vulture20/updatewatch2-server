import { useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi, agentUpdatesApi, certificateAuthorityApi, versionApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import type { AdEncryption, AdminSettings, AgentUpdateStatus, CaRotationStatus, SmtpEncryption, VersionInfo } from '../api/types';

const LOG_LEVELS = ['DEBUG', 'INFO', 'WARNING', 'ERROR'] as const;
const SMTP_ENCRYPTIONS: SmtpEncryption[] = ['None', 'StartTls', 'SslTls'];
const AD_ENCRYPTIONS: AdEncryption[] = ['None', 'StartTls', 'Ldaps'];
const TABS = ['general', 'notifications', 'activeDirectory', 'certificates'] as const;
type Tab = (typeof TABS)[number];

type FormState = Omit<
  AdminSettings,
  'smtpPasswordSet' | 'smtpConfigured' | 'adBindPasswordSet' | 'adConfigured' | 'gitHubTokenSet'
> & {
  smtpPassword: string;
  adBindPassword: string;
  gitHubToken: string;
};

function toFormState(settings: AdminSettings): FormState {
  const {
    smtpPasswordSet: _smtpPasswordSet,
    smtpConfigured: _smtpConfigured,
    adBindPasswordSet: _adBindPasswordSet,
    adConfigured: _adConfigured,
    gitHubTokenSet: _gitHubTokenSet,
    ...rest
  } = settings;
  return { ...rest, smtpPassword: '', adBindPassword: '', gitHubToken: '' };
}

/**
 * There's no test-mail button and no "test connection" button for AD —
 * both would need their own endpoints on top of
 * IEmailNotificationService/IActiveDirectoryAuthService, out of scope for
 * settings persistence itself.
 */
export function AdminPage() {
  const { t, i18n } = useTranslation();
  const [version, setVersion] = useState<VersionInfo | null>(null);
  const [form, setForm] = useState<FormState | null>(null);
  const [tab, setTab] = useState<Tab>('general');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedMessage, setSavedMessage] = useState(false);
  const [caStatus, setCaStatus] = useState<CaRotationStatus | null>(null);
  const [caError, setCaError] = useState<string | null>(null);
  const [caBusy, setCaBusy] = useState(false);
  const [agentUpdateStatus, setAgentUpdateStatus] = useState<AgentUpdateStatus | null>(null);

  const reloadCaStatus = () =>
    certificateAuthorityApi
      .getStatus()
      .then(setCaStatus)
      .catch(() => setCaStatus(null));

  useEffect(() => {
    versionApi.get().then(setVersion).catch(() => setVersion(null));
    adminApi.getSettings().then((settings) => setForm(toFormState(settings)));
    reloadCaStatus();
    agentUpdatesApi.getStatus().then(setAgentUpdateStatus).catch(() => setAgentUpdateStatus(null));
  }, []);

  const runCaAction = (confirmKey: string | null, action: () => Promise<CaRotationStatus>) => {
    if (confirmKey && !window.confirm(t(confirmKey))) {
      return;
    }
    setCaError(null);
    setCaBusy(true);
    action()
      .then(setCaStatus)
      .catch((err) => setCaError(err instanceof ApiError ? err.message : t('login.genericError')))
      .finally(() => setCaBusy(false));
  };

  if (!form) {
    return (
      <section>
        <h1>{t('admin.title')}</h1>
      </section>
    );
  }

  const update = <K extends keyof FormState>(key: K, value: FormState[K]) => setForm({ ...form, [key]: value });

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setSavedMessage(false);
    setSaving(true);
    try {
      const settings = await adminApi.updateSettings({
        ...form,
        smtpPassword: form.smtpPassword === '' ? undefined : form.smtpPassword,
        adBindPassword: form.adBindPassword === '' ? undefined : form.adBindPassword,
        gitHubToken: form.gitHubToken === '' ? undefined : form.gitHubToken,
      });
      setForm(toFormState(settings));
      setSavedMessage(true);
      agentUpdatesApi.getStatus().then(setAgentUpdateStatus).catch(() => setAgentUpdateStatus(null));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('login.genericError'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <section>
      <h1>{t('admin.title')}</h1>

      {version && (
        <dl>
          <dt>{t('admin.serverVersion')}</dt>
          <dd>{version.server}</dd>
          <dt>{t('admin.protocolVersion')}</dt>
          <dd>{version.protocol}</dd>
          <dt>{t('admin.databaseVersion')}</dt>
          <dd>{version.database}</dd>
        </dl>
      )}

      <form onSubmit={(event) => void handleSubmit(event)}>
        {error && <div role="alert" className="login-error">{error}</div>}
        {savedMessage && <div role="status">{t('admin.saved')}</div>}

        <div role="tablist" className="admin-tabs">
          {TABS.map((tabName) => (
            <button
              key={tabName}
              type="button"
              role="tab"
              aria-selected={tab === tabName}
              className={tab === tabName ? 'admin-tab admin-tab-active' : 'admin-tab'}
              onClick={() => setTab(tabName)}
            >
              {t(`admin.tabs.${tabName}`)}
            </button>
          ))}
        </div>

        {/* hidden, not unmounted, so switching tabs never loses edits made
            on another tab — the whole form submits together regardless of
            which tab is active. */}
        <div hidden={tab !== 'general'}>
          <label>
            {t('admin.logLevel')}
            <select value={form.logLevel} onChange={(e) => update('logLevel', e.target.value)}>
              {LOG_LEVELS.map((level) => (
                <option key={level} value={level}>
                  {level}
                </option>
              ))}
            </select>
          </label>
          <p className="field-hint">{t('admin.logLevelHint')}</p>

          <h2>{t('admin.bruteForce')}</h2>
          <label>
            {t('admin.bruteForceMaxAttempts')}
            <input
              type="number"
              min={1}
              value={form.bruteForceMaxAttempts}
              onChange={(e) => update('bruteForceMaxAttempts', Number(e.target.value))}
            />
          </label>
          <label>
            {t('admin.bruteForceWindowMinutes')}
            <input
              type="number"
              min={1}
              value={form.bruteForceWindowMinutes}
              onChange={(e) => update('bruteForceWindowMinutes', Number(e.target.value))}
            />
          </label>
          <label>
            {t('admin.bruteForceLockoutMinutes')}
            <input
              type="number"
              min={1}
              value={form.bruteForceLockoutMinutes}
              onChange={(e) => update('bruteForceLockoutMinutes', Number(e.target.value))}
            />
          </label>

          <h2>{t('admin.agentAutoUpdate.title')}</h2>
          <p className="field-hint">{t('admin.agentAutoUpdate.hint')}</p>
          <label>
            <input
              type="checkbox"
              checked={form.agentAutoUpdateEnabled}
              onChange={(e) => update('agentAutoUpdateEnabled', e.target.checked)}
            />
            {t('admin.agentAutoUpdate.enabled')}
          </label>
          <label>
            {t('admin.agentAutoUpdate.checkIntervalHours')}
            <input
              type="number"
              min={1}
              value={form.agentAutoUpdateCheckIntervalHours}
              onChange={(e) => update('agentAutoUpdateCheckIntervalHours', Number(e.target.value))}
            />
          </label>
          <p className="field-hint">{t('admin.agentAutoUpdate.checkIntervalHoursHint')}</p>
          <label>
            {t('admin.agentAutoUpdate.gitHubToken')}
            <input
              type="password"
              autoComplete="new-password"
              placeholder={t('admin.passwordPlaceholder') ?? ''}
              onChange={(e) => update('gitHubToken', e.target.value)}
            />
          </label>
          <p className="field-hint">{t('admin.agentAutoUpdate.gitHubTokenHint')}</p>
          {agentUpdateStatus && (
            <dl>
              <dt>{t('admin.agentAutoUpdate.latestVersion')}</dt>
              <dd>{agentUpdateStatus.latestVersion ?? t('admin.agentAutoUpdate.noneYet')}</dd>
              <dt>{t('admin.agentAutoUpdate.checkedAt')}</dt>
              <dd>{agentUpdateStatus.checkedAt ? new Date(agentUpdateStatus.checkedAt).toLocaleString(i18n.language) : '—'}</dd>
              {agentUpdateStatus.lastError && (
                <>
                  <dt>{t('admin.agentAutoUpdate.lastError')}</dt>
                  <dd role="alert">{agentUpdateStatus.lastError}</dd>
                </>
              )}
            </dl>
          )}
        </div>

        <div hidden={tab !== 'notifications'}>
          <h2>{t('admin.notifications')}</h2>
          <label>
            {t('admin.smtpHost')}
            <input type="text" value={form.smtpHost} onChange={(e) => update('smtpHost', e.target.value)} />
          </label>
          <label>
            {t('admin.smtpPort')}
            <input
              type="number"
              min={1}
              max={65535}
              value={form.smtpPort}
              onChange={(e) => update('smtpPort', Number(e.target.value))}
            />
          </label>
          <label>
            {t('admin.smtpUsername')}
            <input
              type="text"
              value={form.smtpUsername ?? ''}
              onChange={(e) => update('smtpUsername', e.target.value || null)}
            />
          </label>
          <label>
            {t('admin.smtpPassword')}
            <input
              type="password"
              autoComplete="new-password"
              value={form.smtpPassword}
              placeholder={t('admin.passwordPlaceholder') ?? ''}
              onChange={(e) => update('smtpPassword', e.target.value)}
            />
          </label>
          <label>
            {t('admin.smtpEncryption')}
            <select
              value={form.smtpEncryption}
              onChange={(e) => update('smtpEncryption', e.target.value as SmtpEncryption)}
            >
              {SMTP_ENCRYPTIONS.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>
          <label>
            {t('admin.smtpFromAddress')}
            <input type="email" value={form.smtpFromAddress} onChange={(e) => update('smtpFromAddress', e.target.value)} />
          </label>
          <label>
            {t('admin.smtpFromName')}
            <input type="text" value={form.smtpFromName} onChange={(e) => update('smtpFromName', e.target.value)} />
          </label>
          <label>
            {t('admin.notificationUpdatesPerMachine')}
            <input
              type="number"
              min={1}
              value={form.notificationUpdatesPerMachineThreshold}
              onChange={(e) => update('notificationUpdatesPerMachineThreshold', Number(e.target.value))}
            />
          </label>
          <label>
            {t('admin.notificationAffectedMachines')}
            <input
              type="number"
              min={1}
              value={form.notificationAffectedMachinesThreshold}
              onChange={(e) => update('notificationAffectedMachinesThreshold', Number(e.target.value))}
            />
          </label>
        </div>

        <div hidden={tab !== 'activeDirectory'}>
          <h2>{t('admin.tabs.activeDirectory')}</h2>
          <label>
            <input type="checkbox" checked={form.adEnabled} onChange={(e) => update('adEnabled', e.target.checked)} />
            {t('admin.adEnabled')}
          </label>
          <label>
            {t('admin.adHost')}
            <input type="text" value={form.adHost} onChange={(e) => update('adHost', e.target.value)} />
          </label>
          <label>
            {t('admin.adPort')}
            <input
              type="number"
              min={1}
              max={65535}
              value={form.adPort}
              onChange={(e) => update('adPort', Number(e.target.value))}
            />
          </label>
          <label>
            {t('admin.adEncryption')}
            <select value={form.adEncryption} onChange={(e) => update('adEncryption', e.target.value as AdEncryption)}>
              {AD_ENCRYPTIONS.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>
          <label>
            {t('admin.adBindDn')}
            <input type="text" value={form.adBindDn} onChange={(e) => update('adBindDn', e.target.value)} />
          </label>
          <p className="field-hint">{t('admin.adBindDnHint')}</p>
          <label>
            {t('admin.adBindPassword')}
            <input
              type="password"
              autoComplete="new-password"
              value={form.adBindPassword}
              placeholder={t('admin.passwordPlaceholder') ?? ''}
              onChange={(e) => update('adBindPassword', e.target.value)}
            />
          </label>
          <label>
            {t('admin.adBaseDn')}
            <input type="text" value={form.adBaseDn} onChange={(e) => update('adBaseDn', e.target.value)} />
          </label>
          <label>
            {t('admin.adUserSearchFilter')}
            <input
              type="text"
              value={form.adUserSearchFilter}
              onChange={(e) => update('adUserSearchFilter', e.target.value)}
            />
          </label>
          <p className="field-hint">{t('admin.adUserSearchFilterHint')}</p>
          <label>
            {t('admin.adLoginGroupDn')}
            <input type="text" value={form.adLoginGroupDn} onChange={(e) => update('adLoginGroupDn', e.target.value)} />
          </label>
          <p className="field-hint">{t('admin.adLoginGroupDnHint')}</p>
        </div>

        <div hidden={tab !== 'certificates'}>
          <h2>{t('admin.tabs.certificates')}</h2>
          <label>
            {t('admin.agentCertificateValidityDays')}
            <input
              type="number"
              min={1}
              max={3650}
              value={form.agentCertificateValidityDays}
              onChange={(e) => update('agentCertificateValidityDays', Number(e.target.value))}
            />
          </label>
          <p className="field-hint">{t('admin.agentCertificateValidityDaysHint')}</p>

          <h2>{t('admin.caRotation.title')}</h2>
          <p className="field-hint">{t('admin.caRotation.hint')}</p>
          {caError && <div role="alert" className="login-error">{caError}</div>}
          {caStatus && (
            <>
              <dl>
                <dt>{t('admin.caRotation.current')}</dt>
                <dd>{`${caStatus.currentThumbprint} (${t('admin.caRotation.expires')} ${new Date(caStatus.currentNotAfter).toLocaleDateString(i18n.language)})`}</dd>
                <dt>{t('admin.caRotation.previous')}</dt>
                <dd>
                  {caStatus.previousThumbprint
                    ? `${caStatus.previousThumbprint} (${t('admin.caRotation.expires')} ${new Date(caStatus.previousNotAfter!).toLocaleDateString(i18n.language)})`
                    : '—'}
                </dd>
                <dt>{t('admin.caRotation.pending')}</dt>
                <dd>
                  {caStatus.pendingThumbprint
                    ? `${caStatus.pendingThumbprint} (${t('admin.caRotation.expires')} ${new Date(caStatus.pendingNotAfter!).toLocaleDateString(i18n.language)})`
                    : '—'}
                </dd>
              </dl>

              <button
                type="button"
                disabled={caBusy || caStatus.pendingThumbprint !== null}
                onClick={() => runCaAction(null, certificateAuthorityApi.prepareRotation)}
              >
                {t('admin.caRotation.prepare')}
              </button>{' '}
              <button
                type="button"
                disabled={caBusy || caStatus.pendingThumbprint === null}
                onClick={() => runCaAction('admin.caRotation.activateConfirm', certificateAuthorityApi.activateRotation)}
              >
                {t('admin.caRotation.activate')}
              </button>{' '}
              <button
                type="button"
                disabled={caBusy || caStatus.previousThumbprint === null}
                onClick={() => runCaAction('admin.caRotation.retireConfirm', certificateAuthorityApi.retirePreviousRoot)}
              >
                {t('admin.caRotation.retirePrevious')}
              </button>
            </>
          )}
        </div>

        <button type="submit" disabled={saving}>
          {t('admin.save')}
        </button>
      </form>
    </section>
  );
}
