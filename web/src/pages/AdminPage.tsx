import { useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi, versionApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import type { AdminSettings, SmtpEncryption, VersionInfo } from '../api/types';

const LOG_LEVELS = ['DEBUG', 'INFO', 'WARNING', 'ERROR'] as const;
const SMTP_ENCRYPTIONS: SmtpEncryption[] = ['None', 'StartTls', 'SslTls'];

type FormState = Omit<AdminSettings, 'smtpPasswordSet' | 'smtpConfigured'> & { smtpPassword: string };

function toFormState(settings: AdminSettings): FormState {
  const { smtpPasswordSet: _smtpPasswordSet, smtpConfigured: _smtpConfigured, ...rest } = settings;
  return { ...rest, smtpPassword: '' };
}

/**
 * The AD-connection tab from CLAUDE.md section 6.1 isn't represented yet
 * (see updatewatch2-server#2), and there's no test-mail button — sending a
 * test email needs its own endpoint on top of IEmailNotificationService,
 * which is out of scope for settings persistence itself.
 */
export function AdminPage() {
  const { t } = useTranslation();
  const [version, setVersion] = useState<VersionInfo | null>(null);
  const [form, setForm] = useState<FormState | null>(null);
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
            placeholder={t('admin.smtpPasswordPlaceholder') ?? ''}
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

        <button type="submit" disabled={saving}>
          {t('admin.save')}
        </button>
      </form>
    </section>
  );
}
