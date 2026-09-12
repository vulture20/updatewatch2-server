import { useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { announceAdminSettingsSaved } from '../adminSettingsEvents';
import { adminApi, agentUpdatesApi, certificateAuthorityApi, notificationsApi, updateFiltersApi, versionApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { AuditLogTab } from '../components/AuditLogTab';
import type { AdEncryption, AdminSettings, AgentUpdateStatus, CaRotationStatus, SmtpEncryption, UpdateFilter, VersionInfo } from '../api/types';

const LOG_LEVELS = ['DEBUG', 'INFO', 'WARNING', 'ERROR'] as const;
const SMTP_ENCRYPTIONS: SmtpEncryption[] = ['None', 'StartTls', 'SslTls'];
const AD_ENCRYPTIONS: AdEncryption[] = ['None', 'StartTls', 'Ldaps'];
// Matches AdminController's server-side ValidAuditLogRetentionDays exactly —
// 0 is the "unlimited, never discard" sentinel, listed last since it reads
// more naturally as the final, most-permissive step in the dropdown.
const AUDIT_LOG_RETENTION_DAYS_OPTIONS = [30, 60, 90, 180, 365, 0] as const;
const TABS = ['general', 'notifications', 'activeDirectory', 'certificates', 'updateFilters', 'auditLog', 'info'] as const;
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
  const [searchParams] = useSearchParams();
  const [version, setVersion] = useState<VersionInfo | null>(null);
  const [form, setForm] = useState<FormState | null>(null);
  // Lets SmtpWarningBanner's "Configure SMTP" link land straight on the
  // Notifications tab (?tab=notifications) instead of always opening on
  // General — matches the source mockup's own goSmtpSettings behavior.
  const requestedTab = searchParams.get('tab');
  const [tab, setTab] = useState<Tab>(
    requestedTab && (TABS as readonly string[]).includes(requestedTab) ? (requestedTab as Tab) : 'general',
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedMessage, setSavedMessage] = useState(false);
  const [caStatus, setCaStatus] = useState<CaRotationStatus | null>(null);
  const [caError, setCaError] = useState<string | null>(null);
  const [caBusy, setCaBusy] = useState(false);
  const [agentUpdateStatus, setAgentUpdateStatus] = useState<AgentUpdateStatus | null>(null);
  const [agentUpdateBusy, setAgentUpdateBusy] = useState(false);
  const [agentUpdateError, setAgentUpdateError] = useState<string | null>(null);
  const [manualUploadFiles, setManualUploadFiles] = useState<File[]>([]);
  const [manualUploadBusy, setManualUploadBusy] = useState(false);
  const [manualUploadError, setManualUploadError] = useState<string | null>(null);
  // Remounts the (uncontrolled) file input on success so its own visible
  // file-name list clears along with manualUploadFiles — a plain state
  // reset alone doesn't touch what a <input type="file"> displays.
  const [manualUploadInputKey, setManualUploadInputKey] = useState(0);
  const [updateFilters, setUpdateFilters] = useState<UpdateFilter[]>([]);
  const [updateFiltersError, setUpdateFiltersError] = useState<string | null>(null);
  const [newFilterName, setNewFilterName] = useState('');
  const [newFilterPattern, setNewFilterPattern] = useState('');
  const [editingFilterId, setEditingFilterId] = useState<number | null>(null);
  const [editFilterName, setEditFilterName] = useState('');
  const [editFilterPattern, setEditFilterPattern] = useState('');
  const [testEmailAddress, setTestEmailAddress] = useState('');
  const [testEmailBusy, setTestEmailBusy] = useState(false);
  const [testEmailResult, setTestEmailResult] = useState<'success' | null>(null);
  const [testEmailError, setTestEmailError] = useState<string | null>(null);

  const sendTestEmail = () => {
    setTestEmailError(null);
    setTestEmailResult(null);
    setTestEmailBusy(true);
    notificationsApi
      .testEmail(testEmailAddress)
      .then(() => setTestEmailResult('success'))
      .catch((err) => setTestEmailError(err instanceof ApiError ? err.message : t('login.genericError')))
      .finally(() => setTestEmailBusy(false));
  };

  const reloadCaStatus = () =>
    certificateAuthorityApi
      .getStatus()
      .then(setCaStatus)
      .catch(() => setCaStatus(null));

  const reloadUpdateFilters = () => updateFiltersApi.list().then(setUpdateFilters).catch(() => setUpdateFilters([]));

  useEffect(() => {
    versionApi.get().then(setVersion).catch(() => setVersion(null));
    adminApi.getSettings().then((settings) => setForm(toFormState(settings)));
    reloadCaStatus();
    agentUpdatesApi.getStatus().then(setAgentUpdateStatus).catch(() => setAgentUpdateStatus(null));
    reloadUpdateFilters();
  }, []);

  const addUpdateFilter = () => {
    setUpdateFiltersError(null);
    updateFiltersApi
      .create({ name: newFilterName, pattern: newFilterPattern })
      .then(() => {
        setNewFilterName('');
        setNewFilterPattern('');
        reloadUpdateFilters();
      })
      .catch((err) => setUpdateFiltersError(err instanceof ApiError ? err.message : t('login.genericError')));
  };

  const startEditingUpdateFilter = (filter: UpdateFilter) => {
    setUpdateFiltersError(null);
    setEditingFilterId(filter.id);
    setEditFilterName(filter.name);
    setEditFilterPattern(filter.pattern);
  };

  const saveUpdateFilterEdit = () => {
    if (editingFilterId === null) {
      return;
    }
    setUpdateFiltersError(null);
    updateFiltersApi
      .update(editingFilterId, { name: editFilterName, pattern: editFilterPattern })
      .then(() => {
        setEditingFilterId(null);
        reloadUpdateFilters();
      })
      .catch((err) => setUpdateFiltersError(err instanceof ApiError ? err.message : t('login.genericError')));
  };

  const deleteUpdateFilter = (filter: UpdateFilter) => {
    if (!window.confirm(t('admin.updateFilters.deleteConfirm', { name: filter.name }))) {
      return;
    }
    setUpdateFiltersError(null);
    updateFiltersApi
      .delete(filter.id)
      .then(reloadUpdateFilters)
      .catch((err) => setUpdateFiltersError(err instanceof ApiError ? err.message : t('login.genericError')));
  };

  const runAgentUpdateCheck = () => {
    setAgentUpdateError(null);
    setAgentUpdateBusy(true);
    agentUpdatesApi
      .checkNow()
      .then(setAgentUpdateStatus)
      .catch((err) => setAgentUpdateError(err instanceof ApiError ? err.message : t('login.genericError')))
      .finally(() => setAgentUpdateBusy(false));
  };

  const runManualUpload = () => {
    setManualUploadError(null);
    setManualUploadBusy(true);
    agentUpdatesApi
      .upload(manualUploadFiles)
      .then((status) => {
        setAgentUpdateStatus(status);
        setManualUploadFiles([]);
        setManualUploadInputKey((key) => key + 1);
      })
      .catch((err) => setManualUploadError(err instanceof ApiError ? err.message : t('login.genericError')))
      .finally(() => setManualUploadBusy(false));
  };

  const runCaAction = (
    confirmKey: string | null,
    action: () => Promise<CaRotationStatus>,
    confirmOptions?: Record<string, unknown>,
  ) => {
    if (confirmKey && !window.confirm(t(confirmKey, confirmOptions))) {
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

  // One shared card shape for every certificate shown on the Info tab (CA
  // root — current/previous/pending — plus the server's own agent-facing
  // leaf) rather than repeating the same <dl> four times.
  const certCard = (
    kicker: string,
    subject: string,
    issuer: string,
    serialNumber: string,
    thumbprint: string,
    notBefore: string,
    notAfter: string,
  ) => (
    <div className="card" key={kicker}>
      <span className="card-kicker">{kicker}</span>
      <dl>
        <dt className="text-muted">{t('admin.info.certificates.subject')}</dt>
        <dd>{subject}</dd>
        <dt className="text-muted">{t('admin.info.certificates.issuer')}</dt>
        <dd>{issuer}</dd>
        <dt className="text-muted">{t('admin.info.certificates.serialNumber')}</dt>
        <dd>{serialNumber}</dd>
        <dt className="text-muted">{t('admin.info.certificates.thumbprint')}</dt>
        <dd>{thumbprint}</dd>
        <dt className="text-muted">{t('admin.info.certificates.issued')}</dt>
        <dd>{new Date(notBefore).toLocaleString(i18n.language)}</dd>
        <dt className="text-muted">{t('admin.info.certificates.expires')}</dt>
        <dd>{new Date(notAfter).toLocaleString(i18n.language)}</dd>
      </dl>
    </div>
  );

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
      announceAdminSettingsSaved({ smtpConfigured: settings.smtpConfigured });
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

      <form onSubmit={(event) => void handleSubmit(event)}>
        {error && <div role="alert" className="login-error">{error}</div>}

        <div className="admin-layout">
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

          <div>
        {/* hidden, not unmounted, so switching tabs never loses edits made
            on another tab — the whole form submits together regardless of
            which tab is active. */}
        <div hidden={tab !== 'general'} className="tab-panel">
          <div className="card">
            <span className="card-kicker">{t('admin.tabs.general')}</span>
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
          </div>

          <div className="card">
            <span className="card-kicker">{t('admin.bruteForce')}</span>
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
          </div>

          <div className="card">
            <span className="card-kicker">{t('admin.agentAutoUpdate.title')}</span>
            <p className="card-body">{t('admin.agentAutoUpdate.hint')}</p>
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
              <>
                <dl>
                  <dt>{t('admin.agentAutoUpdate.latestVersion')}</dt>
                  <dd>{agentUpdateStatus.latestVersion ?? t('admin.agentAutoUpdate.noneYet')}</dd>
                  {agentUpdateStatus.latestVersion && (
                    <>
                      <dt>{t('admin.agentAutoUpdate.source')}</dt>
                      <dd>
                        {agentUpdateStatus.manuallyUploaded
                          ? t('admin.agentAutoUpdate.sourceManual')
                          : t('admin.agentAutoUpdate.sourceGitHub')}
                      </dd>
                    </>
                  )}
                  <dt>{t('admin.agentAutoUpdate.checkedAt')}</dt>
                  <dd>{agentUpdateStatus.checkedAt ? new Date(agentUpdateStatus.checkedAt).toLocaleString(i18n.language) : '—'}</dd>
                  {agentUpdateStatus.lastError && (
                    <>
                      <dt>{t('admin.agentAutoUpdate.lastError')}</dt>
                      <dd role="alert">{agentUpdateStatus.lastError}</dd>
                    </>
                  )}
                </dl>
                {agentUpdateError && <div role="alert" className="login-error">{agentUpdateError}</div>}
                <button
                  type="button"
                  disabled={agentUpdateBusy || !agentUpdateStatus.enabled}
                  onClick={runAgentUpdateCheck}
                >
                  {agentUpdateBusy ? t('admin.agentAutoUpdate.checking') : t('admin.agentAutoUpdate.checkNow')}
                </button>
                {!agentUpdateStatus.enabled && <p className="field-hint">{t('admin.agentAutoUpdate.checkNowDisabledHint')}</p>}

                <span className="card-kicker">{t('admin.agentAutoUpdate.manualUpload.title')}</span>
                <p className="field-hint">{t('admin.agentAutoUpdate.manualUpload.hint')}</p>
                <label>
                  {t('admin.agentAutoUpdate.manualUpload.files')}
                  <input
                    key={manualUploadInputKey}
                    type="file"
                    multiple
                    accept=".exe,.deb,.rpm"
                    onChange={(e) => setManualUploadFiles(e.target.files ? Array.from(e.target.files) : [])}
                  />
                </label>
                <p className="field-hint">{t('admin.agentAutoUpdate.manualUpload.filesHint')}</p>
                {manualUploadError && <div role="alert" className="login-error">{manualUploadError}</div>}
                <button
                  type="button"
                  disabled={manualUploadBusy || !agentUpdateStatus.enabled || manualUploadFiles.length === 0}
                  onClick={runManualUpload}
                >
                  {manualUploadBusy ? t('admin.agentAutoUpdate.manualUpload.uploading') : t('admin.agentAutoUpdate.manualUpload.upload')}
                </button>
              </>
            )}
          </div>

          <div className="tab-save-row">
            <button type="submit" className="btn-accent" disabled={saving}>
              {t('admin.save')}
            </button>
            {savedMessage && <span className="saved-message" role="status">{t('admin.saved')}</span>}
          </div>
        </div>

        <div hidden={tab !== 'notifications'} className="tab-panel">
          <div className="card">
          <span className="card-kicker">{t('admin.notifications')}</span>
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
            {t('admin.notificationRecipientAddress')}
            <input
              type="email"
              value={form.notificationRecipientAddress ?? ''}
              onChange={(e) => update('notificationRecipientAddress', e.target.value || null)}
            />
          </label>
          <p className="field-hint">{t('admin.notificationRecipientAddressHint')}</p>
          <label>
            {t('admin.testEmailAddress')}
            <input type="email" value={testEmailAddress} onChange={(e) => setTestEmailAddress(e.target.value)} />
          </label>
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-3)' }}>
            <button type="button" disabled={testEmailBusy || !testEmailAddress} onClick={sendTestEmail}>
              {testEmailBusy ? t('admin.testEmailSending') : t('admin.testEmailSend')}
            </button>
            {testEmailResult === 'success' && <span className="saved-message" role="status">{t('admin.testEmailSuccess')}</span>}
          </div>
          {testEmailError && <div role="alert" className="login-error">{testEmailError}</div>}
          </div>

          <div className="card">
          <span className="card-kicker">{t('admin.certificateExpiryNotifications.title')}</span>
          <label>
            <input
              type="checkbox"
              checked={form.certificateExpiryNotificationsEnabled}
              onChange={(e) => update('certificateExpiryNotificationsEnabled', e.target.checked)}
            />
            {t('admin.certificateExpiryNotifications.enabled')}
          </label>
          <p className="field-hint">{t('admin.certificateExpiryNotifications.hint')}</p>
          </div>

          <div className="card">
          <span className="card-kicker">{t('admin.notificationThresholds')}</span>
          <label>
            <input
              type="checkbox"
              checked={form.notificationUpdatesPerMachineEnabled}
              onChange={(e) => update('notificationUpdatesPerMachineEnabled', e.target.checked)}
            />
            {t('admin.notificationUpdatesPerMachine')}
          </label>
          <input
            type="number"
            min={1}
            disabled={!form.notificationUpdatesPerMachineEnabled}
            value={form.notificationUpdatesPerMachineThreshold}
            onChange={(e) => update('notificationUpdatesPerMachineThreshold', Number(e.target.value))}
          />
          <label>
            <input
              type="checkbox"
              checked={form.notificationAffectedMachinesEnabled}
              onChange={(e) => update('notificationAffectedMachinesEnabled', e.target.checked)}
            />
            {t('admin.notificationAffectedMachines')}
          </label>
          <input
            type="number"
            min={1}
            disabled={!form.notificationAffectedMachinesEnabled}
            value={form.notificationAffectedMachinesThreshold}
            onChange={(e) => update('notificationAffectedMachinesThreshold', Number(e.target.value))}
          />
          <p className="field-hint">{t('admin.notificationThresholdsHint')}</p>
          </div>

          <div className="tab-save-row">
            <button type="submit" className="btn-accent" disabled={saving}>
              {t('admin.save')}
            </button>
            {savedMessage && <span className="saved-message" role="status">{t('admin.saved')}</span>}
          </div>
        </div>

        <div hidden={tab !== 'activeDirectory'} className="tab-panel">
          <div className="card">
          <span className="card-kicker">{t('admin.tabs.activeDirectory')}</span>
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

          <div className="tab-save-row tab-save-row-divided">
            <button type="submit" className="btn-accent" disabled={saving}>
              {t('admin.save')}
            </button>
            {savedMessage && <span className="saved-message" role="status">{t('admin.saved')}</span>}
          </div>
          </div>
        </div>

        <div hidden={tab !== 'certificates'} className="tab-panel">
          <div className="card">
          <span className="card-kicker">{t('admin.tabs.certificates')}</span>
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
          <label>
            {t('admin.certificateExpiryWarningLeadDays')}
            <input
              type="number"
              min={1}
              max={365}
              value={form.certificateExpiryWarningLeadDays}
              onChange={(e) => update('certificateExpiryWarningLeadDays', Number(e.target.value))}
            />
          </label>
          <p className="field-hint">{t('admin.certificateExpiryWarningLeadDaysHint')}</p>

          <div className="tab-save-row tab-save-row-divided">
            <button type="submit" className="btn-accent" disabled={saving}>
              {t('admin.save')}
            </button>
            {savedMessage && <span className="saved-message" role="status">{t('admin.saved')}</span>}
          </div>
          </div>

          <div className="card">
          <span className="card-kicker">{t('admin.caRotation.title')}</span>
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

              <p className="field-hint">
                <a href={certificateAuthorityApi.downloadUrl} download="updatewatch2-ca.crt">
                  {t('admin.caRotation.download')}
                </a>
                {' — '}
                {t('admin.caRotation.downloadHint')}
              </p>

              {caStatus.previousThumbprint && (
                <div role="status" className="field-hint">
                  <p>
                    {t('admin.caRotation.stillOnPreviousRoot', { count: caStatus.stillOnPreviousRootCount })}
                    {caStatus.stillOnPreviousRootHostnames.length > 0 && `: ${caStatus.stillOnPreviousRootHostnames.join(', ')}`}
                    {caStatus.stillOnPreviousRootCount > caStatus.stillOnPreviousRootHostnames.length &&
                      ` ${t('admin.caRotation.stillOnPreviousRootMore', { count: caStatus.stillOnPreviousRootCount - caStatus.stillOnPreviousRootHostnames.length })}`}
                  </p>
                  {caStatus.unknownRootAgentCount > 0 && <p>{t('admin.caRotation.unknownRootAgents', { count: caStatus.unknownRootAgentCount })}</p>}
                </div>
              )}

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
                onClick={() =>
                  runCaAction('admin.caRotation.retireConfirm', certificateAuthorityApi.retirePreviousRoot, {
                    count: caStatus.stillOnPreviousRootCount,
                  })
                }
              >
                {t('admin.caRotation.retirePrevious')}
              </button>
            </>
          )}
          </div>
        </div>

        <div hidden={tab !== 'updateFilters'} className="tab-panel">
          <div className="card">
          <span className="card-kicker">{t('admin.updateFilters.title')}</span>
          <p className="field-hint">{t('admin.updateFilters.hint')}</p>
          {updateFiltersError && <div role="alert" className="login-error">{updateFiltersError}</div>}

          {updateFilters.length === 0 ? (
            <p>{t('admin.updateFilters.none')}</p>
          ) : (
            <ul>
              {updateFilters.map((filter) =>
                editingFilterId === filter.id ? (
                  <li key={filter.id}>
                    <input
                      type="text"
                      aria-label={t('admin.updateFilters.name')}
                      value={editFilterName}
                      onChange={(e) => setEditFilterName(e.target.value)}
                    />{' '}
                    <input
                      type="text"
                      aria-label={t('admin.updateFilters.pattern')}
                      value={editFilterPattern}
                      onChange={(e) => setEditFilterPattern(e.target.value)}
                    />{' '}
                    <button type="button" onClick={saveUpdateFilterEdit}>
                      {t('admin.updateFilters.save')}
                    </button>{' '}
                    <button type="button" onClick={() => setEditingFilterId(null)}>
                      {t('admin.updateFilters.cancel')}
                    </button>
                  </li>
                ) : (
                  <li key={filter.id}>
                    <strong>{filter.name}</strong> — <code>{filter.pattern}</code>{' '}
                    <button type="button" onClick={() => startEditingUpdateFilter(filter)}>
                      {t('admin.updateFilters.edit')}
                    </button>{' '}
                    <button type="button" className="btn-ghost" onClick={() => deleteUpdateFilter(filter)}>
                      {t('admin.updateFilters.delete')}
                    </button>
                  </li>
                ),
              )}
            </ul>
          )}

          <label>
            {t('admin.updateFilters.name')}
            <input type="text" value={newFilterName} onChange={(e) => setNewFilterName(e.target.value)} />
          </label>
          <label>
            {t('admin.updateFilters.pattern')}
            <input type="text" value={newFilterPattern} onChange={(e) => setNewFilterPattern(e.target.value)} />
          </label>
          <p className="field-hint">{t('admin.updateFilters.patternHint')}</p>
          <button type="button" className="btn-accent" disabled={!newFilterName || !newFilterPattern} onClick={addUpdateFilter}>
            {t('admin.updateFilters.add')}
          </button>

          <div className="tab-save-row tab-save-row-divided">
            <button type="submit" className="btn-accent" disabled={saving}>
              {t('admin.save')}
            </button>
            {savedMessage && <span className="saved-message" role="status">{t('admin.saved')}</span>}
          </div>
          </div>
        </div>

        <div hidden={tab !== 'auditLog'} className="tab-panel">
          <div className="card">
          <span className="card-kicker">{t('admin.auditLogRetention')}</span>
          <label>
            {t('admin.auditLogRetentionDays')}
            <select
              value={form.auditLogRetentionDays}
              onChange={(e) => update('auditLogRetentionDays', Number(e.target.value))}
            >
              {AUDIT_LOG_RETENTION_DAYS_OPTIONS.map((days) => (
                <option key={days} value={days}>
                  {days === 0 ? t('admin.auditLogRetentionDaysUnlimited') : t('admin.auditLogRetentionDaysOption', { count: days })}
                </option>
              ))}
            </select>
          </label>
          <p className="field-hint">{t('admin.auditLogRetentionDaysHint')}</p>

          <div className="tab-save-row tab-save-row-divided">
            <button type="submit" className="btn-accent" disabled={saving}>
              {t('admin.save')}
            </button>
            {savedMessage && <span className="saved-message" role="status">{t('admin.saved')}</span>}
          </div>
          </div>

          <AuditLogTab />
        </div>

        <div hidden={tab !== 'info'} className="tab-panel">
          <div className="card">
            <span className="card-kicker">{t('admin.tabs.info')}</span>
            {version && (
              <dl>
                <dt className="text-muted">{t('admin.serverVersion')}</dt>
                <dd>{version.server}</dd>
                <dt className="text-muted">{t('admin.protocolVersion')}</dt>
                <dd>{version.protocol}</dd>
                <dt className="text-muted">{t('admin.databaseVersion')}</dt>
                <dd>{version.database}</dd>
              </dl>
            )}
          </div>

          {caStatus && (
            <>
              {certCard(
                t('admin.info.certificates.serverCertificate'),
                caStatus.serverLeafSubject,
                caStatus.serverLeafIssuer,
                caStatus.serverLeafSerialNumber,
                caStatus.serverLeafThumbprint,
                caStatus.serverLeafNotBefore,
                caStatus.serverLeafNotAfter,
              )}
              {certCard(
                t('admin.info.certificates.caRoot'),
                caStatus.currentSubject,
                caStatus.currentIssuer,
                caStatus.currentSerialNumber,
                caStatus.currentThumbprint,
                caStatus.currentNotBefore,
                caStatus.currentNotAfter,
              )}
              {caStatus.previousThumbprint &&
                certCard(
                  t('admin.info.certificates.caRootPrevious'),
                  caStatus.previousSubject!,
                  caStatus.previousIssuer!,
                  caStatus.previousSerialNumber!,
                  caStatus.previousThumbprint,
                  caStatus.previousNotBefore!,
                  caStatus.previousNotAfter!,
                )}
              {caStatus.pendingThumbprint &&
                certCard(
                  t('admin.info.certificates.caRootPending'),
                  caStatus.pendingSubject!,
                  caStatus.pendingIssuer!,
                  caStatus.pendingSerialNumber!,
                  caStatus.pendingThumbprint,
                  caStatus.pendingNotBefore!,
                  caStatus.pendingNotAfter!,
                )}
            </>
          )}
        </div>
          </div>
        </div>
      </form>
    </section>
  );
}
