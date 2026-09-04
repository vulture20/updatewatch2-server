import { useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi, versionApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import type { AdEncryption, AdminSettings, SmtpEncryption, VersionInfo } from '../api/types';

const LOG_LEVELS = ['DEBUG', 'INFO', 'WARNING', 'ERROR'] as const;
const SMTP_ENCRYPTIONS: SmtpEncryption[] = ['None', 'StartTls', 'SslTls'];
const AD_ENCRYPTIONS: AdEncryption[] = ['None', 'StartTls', 'Ldaps'];
const TABS = ['general', 'notifications', 'activeDirectory', 'certificates'] as const;
type Tab = (typeof TABS)[number];

type FormState = Omit<
  AdminSettings,
  'smtpPasswordSet' | 'smtpConfigured' | 'adBindPasswordSet' | 'adConfigured'
> & {
  smtpPassword: string;
  adBindPassword: string;
};

function toFormState(settings: AdminSettings): FormState {
  const {
    smtpPasswordSet: _smtpPasswordSet,
    smtpConfigured: _smtpConfigured,
    adBindPasswordSet: _adBindPasswordSet,
    adConfigured: _adConfigured,
    ...rest
  } = settings;
  return { ...rest, smtpPassword: '', adBindPassword: '' };
}

/**
 * There's no test-mail button and no "test connection" button for AD —
 * both would need their own endpoints on top of
 * IEmailNotificationService/IActiveDirectoryAuthService, out of scope for
 * settings persistence itself.
 */
export function AdminPage() {
  const { t } = useTranslation();
  const [version, setVersion] = useState<VersionInfo | null>(null);
  const [form, setForm] = useState<FormState | null>(null);
  const [tab, setTab] = useState<Tab>('general');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedMessage, setSavedMessage] = useState(false);

  useEffect(() => {
    versionApi.get().then(setVersion).catch(() => setVersion(null));
    adminApi.getSettings().then((settings) => setForm(toFormState(settings)));
  }, []);

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
      });
      setForm(toFormState(settings));
      setSavedMessage(true);
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
        </div>

        <button type="submit" disabled={saving}>
          {t('admin.save')}
        </button>
      </form>
    </section>
  );
}
