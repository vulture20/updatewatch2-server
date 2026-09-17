import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../api/client';
import type { AgentDetail, UpdateAgentSettings } from '../api/types';

const LOG_LEVELS = ['DEBUG', 'INFO', 'WARNING', 'ERROR'];

// Matches AgentOptions' own defaults, agent-side — only ever used as a last
// resort for an agent that has never reported a heartbeat with these
// fields populated at all (Desired* and Actual* both still null).
const DEFAULT_LOG_LEVEL = 'INFO';
const DEFAULT_INTERVAL_MINUTES = 240;
const DEFAULT_JITTER_SECONDS = 300;

const MIN_INTERVAL_MINUTES = 1;
const MAX_INTERVAL_MINUTES = 10_080;
const MIN_JITTER_SECONDS = 0;
const MAX_JITTER_SECONDS = 3_600;

/**
 * Per-agent settings, opened from AgentDetailPage's header via a single
 * "Settings" button (server v1.3.13, at the user's explicit request —
 * "Kannst du in den Agent-Details den Button 'Zertifikat neu ausstellen'
 * durch 'Einstellungen' ersetzen... Hier sollen später auch weitere
 * Einstellungen wie der Alive-Intervall konfigurierbar sein."). Reissue
 * certificate and Delete agent both moved in here from the header's own
 * button row, each with a short explanation. Grew into the actual home for
 * pushed per-agent settings (server v1.3.14/1.3.16): a LogLevel/update-
 * check-interval/jitter form. **Not an optional override** — reframed at
 * the user's explicit follow-up request ("Der aktuelle Wert soll immer im
 * Auswahl- bzw. Textfeld stehen. Änderungen sollen auf beiden Seiten
 * möglich sein und direkt auf die Gegenseite gespiegelt werden.") after
 * the first version left the field blank whenever no explicit override had
 * ever been set, and never reflected a manual local registry/config-file
 * edit back into the field at all. Each field is now always initialized
 * from the agent's own current value (`desired* ?? actual* ?? <agent's own
 * hardcoded default>` — the `desired*` half is basically always populated
 * in practice, since the server auto-adopts a fresh `actual*` into it the
 * moment it first becomes known; see `Agent.DesiredLogLevel`'s doc comment,
 * server-side) and saving always submits a concrete value for all three —
 * there is no longer a way to "clear" a field back to blank. Same
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
  const [logLevel, setLogLevel] = useState(agent.desiredLogLevel ?? agent.actualLogLevel ?? DEFAULT_LOG_LEVEL);
  const [intervalMinutes, setIntervalMinutes] = useState(
    (agent.desiredUpdateCheckIntervalMinutes ?? agent.actualUpdateCheckIntervalMinutes ?? DEFAULT_INTERVAL_MINUTES).toString(),
  );
  const [jitterSeconds, setJitterSeconds] = useState(
    (agent.desiredUpdateCheckJitterSeconds ?? agent.actualUpdateCheckJitterSeconds ?? DEFAULT_JITTER_SECONDS).toString(),
  );
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

  const intervalValue = Number(intervalMinutes);
  const jitterValue = Number(jitterSeconds);
  const intervalValid = intervalMinutes !== '' && Number.isInteger(intervalValue) && intervalValue >= MIN_INTERVAL_MINUTES && intervalValue <= MAX_INTERVAL_MINUTES;
  const jitterValid = jitterSeconds !== '' && Number.isInteger(jitterValue) && jitterValue >= MIN_JITTER_SECONDS && jitterValue <= MAX_JITTER_SECONDS;

  const saveSettings = async () => {
    setSaving(true);
    setSaved(false);
    setError(null);
    try {
      await onSaveSettings({
        desiredLogLevel: logLevel,
        desiredUpdateCheckIntervalMinutes: intervalValue,
        desiredUpdateCheckJitterSeconds: jitterValue,
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
            min={MIN_INTERVAL_MINUTES}
            max={MAX_INTERVAL_MINUTES}
            value={intervalMinutes}
            onChange={(e) => setIntervalMinutes(e.target.value)}
          />
          <p className="field-hint">
            {t('agentDetail.pushedUpdateCheckIntervalMinutesHint', { actual: agent.actualUpdateCheckIntervalMinutes ?? '—' })}
          </p>
        </label>

        <label>
          {t('agentDetail.pushedUpdateCheckJitterSeconds')}
          <input
            type="number"
            min={MIN_JITTER_SECONDS}
            max={MAX_JITTER_SECONDS}
            value={jitterSeconds}
            onChange={(e) => setJitterSeconds(e.target.value)}
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
          <button type="button" className="btn-accent" disabled={saving || !intervalValid || !jitterValid} onClick={() => void saveSettings()}>
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
