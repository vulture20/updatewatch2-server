import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../api/client';
import type { AgentDetail, UpdateAgentSettings } from '../api/types';

const LOG_LEVELS = ['DEBUG', 'INFO', 'WARNING', 'ERROR'];

/**
 * Per-agent settings, opened from AgentDetailPage's header via a single
 * "Settings" button (server v1.3.13, at the user's explicit request —
 * "Kannst du in den Agent-Details den Button 'Zertifikat neu ausstellen'
 * durch 'Einstellungen' ersetzen... Hier sollen später auch weitere
 * Einstellungen wie der Alive-Intervall konfigurierbar sein."). Reissue
 * certificate and Delete agent both moved in here from the header's own
 * button row, each with a short explanation. Grew into the actual home for
 * pushed per-agent settings (server v1.3.14, at the user's explicit
 * request — "LogLevel des Agents über den Server setzen... Diese Logik
 * soll für alle (auch spätere) Einstellungen am Server für den Agent
 * gelten."): a LogLevel/update-check-interval/jitter form, each field
 * showing the agent's own currently-reported actual value alongside the
 * editable override — leaving a field blank clears that override,
 * handing control back to the agent's local registry/config file. Same
 * `.dialog-backdrop`/`.dialog` overlay pattern as OneTimeSecretDialog
 * (backdrop + centered box, Escape or a backdrop click closes it) — not a
 * shared hook yet, since there are only two dialogs in this app and the
 * Escape-handling effect is a few lines each. Unlike OneTimeSecretDialog,
 * this one translates its own labels directly (useTranslation) rather
 * than taking them all as props — the settings-form section alone would
 * otherwise need a dozen more string props for what is a page-specific
 * dialog, not a shared generic primitive.
 */
export function AgentSettingsDialog({
  agent,
  onReissueCertificate,
  onDelete,
  onSaveSettings,
  onClose,
}: {
  agent: AgentDetail;
  // Omitted entirely (not just disabled) for an unapproved agent — it
  // never had a certificate to reissue, matching the header button's own
  // previous approved-only gating.
  onReissueCertificate?: () => void;
  onDelete: () => void;
  onSaveSettings: (settings: UpdateAgentSettings) => Promise<void>;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [logLevel, setLogLevel] = useState(agent.desiredLogLevel ?? '');
  const [intervalMinutes, setIntervalMinutes] = useState(agent.desiredUpdateCheckIntervalMinutes?.toString() ?? '');
  const [jitterSeconds, setJitterSeconds] = useState(agent.desiredUpdateCheckJitterSeconds?.toString() ?? '');
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  const saveSettings = async () => {
    setSaving(true);
    setSaved(false);
    setError(null);
    try {
      await onSaveSettings({
        desiredLogLevel: logLevel === '' ? null : logLevel,
        desiredUpdateCheckIntervalMinutes: intervalMinutes === '' ? null : Number(intervalMinutes),
        desiredUpdateCheckJitterSeconds: jitterSeconds === '' ? null : Number(jitterSeconds),
      });
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('login.genericError'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="dialog-backdrop" onClick={onClose}>
      <div
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="agent-settings-title"
        onClick={(event) => event.stopPropagation()}
      >
        <p id="agent-settings-title" className="dialog-title">
          {t('agentDetail.settings')}
        </p>

        {onReissueCertificate && (
          <div>
            <button type="button" onClick={onReissueCertificate}>
              {t('agentDetail.reissueCertificate')}
            </button>
            <p className="field-hint">{t('agentDetail.reissueCertificateHint')}</p>
          </div>
        )}

        <div>
          <button type="button" onClick={onDelete}>
            {t('agentDetail.delete')}
          </button>
          <p className="field-hint">{t('agentDetail.deleteHint')}</p>
        </div>

        <label>
          {t('agentDetail.pushedLogLevel')}
          <select value={logLevel} onChange={(e) => setLogLevel(e.target.value)}>
            <option value="">{t('agentDetail.noOverride')}</option>
            {LOG_LEVELS.map((level) => (
              <option key={level} value={level}>
                {level}
              </option>
            ))}
          </select>
          <p className="field-hint">{t('agentDetail.pushedLogLevelHint', { actual: agent.actualLogLevel ?? '—' })}</p>
        </label>

        <label>
          {t('agentDetail.pushedUpdateCheckIntervalMinutes')}
          <input
            type="number"
            min={1}
            value={intervalMinutes}
            onChange={(e) => setIntervalMinutes(e.target.value)}
            placeholder={t('agentDetail.noOverride')}
          />
          <p className="field-hint">
            {t('agentDetail.pushedUpdateCheckIntervalMinutesHint', { actual: agent.actualUpdateCheckIntervalMinutes ?? '—' })}
          </p>
        </label>

        <label>
          {t('agentDetail.pushedUpdateCheckJitterSeconds')}
          <input
            type="number"
            min={0}
            value={jitterSeconds}
            onChange={(e) => setJitterSeconds(e.target.value)}
            placeholder={t('agentDetail.noOverride')}
          />
          <p className="field-hint">
            {t('agentDetail.pushedUpdateCheckJitterSecondsHint', { actual: agent.actualUpdateCheckJitterSeconds ?? '—' })}
          </p>
        </label>

        {error && (
          <p role="alert" className="login-error">
            {error}
          </p>
        )}

        <div className="dialog-actions">
          {saved && (
            <span className="saved-message" role="status">
              {t('admin.saved')}
            </span>
          )}
          <button type="button" className="btn-accent" disabled={saving} onClick={() => void saveSettings()}>
            {t('agentDetail.saveSettings')}
          </button>
          <button type="button" onClick={onClose}>
            {t('agentDetail.close')}
          </button>
        </div>
      </div>
    </div>
  );
}
