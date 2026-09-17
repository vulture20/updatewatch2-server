import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../api/client';
import type { BulkAgentSettingsUpdate } from '../api/types';

const LOG_LEVELS = ['DEBUG', 'INFO', 'WARNING', 'ERROR'];

// Same defaults/bounds AgentSettingsDialog uses for the single-agent form —
// only ever shown as a starting point here, since there is no single
// "current value" to pre-fill from once more than one agent is selected.
const DEFAULT_LOG_LEVEL = 'INFO';
const DEFAULT_INTERVAL_MINUTES = 240;
const DEFAULT_JITTER_SECONDS = 300;
const DEFAULT_ALIVE_INTERVAL_MINUTES = 5;

const MIN_INTERVAL_MINUTES = 1;
const MAX_INTERVAL_MINUTES = 10_080;
const MIN_JITTER_SECONDS = 0;
const MAX_JITTER_SECONDS = 3_600;
const MIN_ALIVE_INTERVAL_MINUTES = 1;
const MAX_ALIVE_INTERVAL_MINUTES = 1_440;

/**
 * Bulk counterpart to AgentSettingsDialog, opened from AgentsListPage's
 * multi-select toolbar via a new "Einstellungen"/"Settings" button
 * (server v1.3.20), at the user's explicit request ("Es fehlt außerdem die
 * Möglichkeit Agent-Einstellungen bulk zu pushen. Dies könnte in der
 * Agent-Übersicht geschehen und über ein Fenster, wie bei den
 * Agent-Einstellungen, gelöst werden."). Unlike the single-agent dialog,
 * every field here has its own checkbox and starts unchecked/disabled —
 * confirmed via an explicit clarifying question before implementing: the
 * user chose a per-field opt-in over always pushing all four settings to
 * every selected agent, specifically so an admin can push just one setting
 * (e.g. LogLevel=DEBUG on several agents at once for diagnosis) without
 * being forced to also overwrite the others' individually-tuned values.
 * There is deliberately no "current value" hint text here (unlike the
 * single-agent dialog) — the selected agents can easily have different
 * current values, so showing any one of them next to a shared input would
 * be misleading. Same `.dialog-backdrop`/`.dialog` overlay pattern as
 * AgentSettingsDialog/OneTimeSecretDialog.
 */
export function AgentBulkSettingsDialog({
  hostnames,
  onSave,
  onClose,
}: {
  hostnames: string[];
  onSave: (update: BulkAgentSettingsUpdate) => Promise<void>;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [logLevelEnabled, setLogLevelEnabled] = useState(false);
  const [logLevel, setLogLevel] = useState(DEFAULT_LOG_LEVEL);
  const [intervalEnabled, setIntervalEnabled] = useState(false);
  const [intervalMinutes, setIntervalMinutes] = useState(DEFAULT_INTERVAL_MINUTES.toString());
  const [jitterEnabled, setJitterEnabled] = useState(false);
  const [jitterSeconds, setJitterSeconds] = useState(DEFAULT_JITTER_SECONDS.toString());
  const [aliveIntervalEnabled, setAliveIntervalEnabled] = useState(false);
  const [aliveIntervalMinutes, setAliveIntervalMinutes] = useState(DEFAULT_ALIVE_INTERVAL_MINUTES.toString());
  const [saving, setSaving] = useState(false);
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
  const aliveIntervalValue = Number(aliveIntervalMinutes);
  const intervalValid = !intervalEnabled || (intervalMinutes !== '' && Number.isInteger(intervalValue) && intervalValue >= MIN_INTERVAL_MINUTES && intervalValue <= MAX_INTERVAL_MINUTES);
  const jitterValid = !jitterEnabled || (jitterSeconds !== '' && Number.isInteger(jitterValue) && jitterValue >= MIN_JITTER_SECONDS && jitterValue <= MAX_JITTER_SECONDS);
  const aliveIntervalValid =
    !aliveIntervalEnabled || (aliveIntervalMinutes !== '' && Number.isInteger(aliveIntervalValue) && aliveIntervalValue >= MIN_ALIVE_INTERVAL_MINUTES && aliveIntervalValue <= MAX_ALIVE_INTERVAL_MINUTES);
  const anyFieldEnabled = logLevelEnabled || intervalEnabled || jitterEnabled || aliveIntervalEnabled;

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      await onSave({
        hostnames,
        desiredLogLevel: logLevelEnabled ? logLevel : undefined,
        desiredUpdateCheckIntervalMinutes: intervalEnabled ? intervalValue : undefined,
        desiredUpdateCheckJitterSeconds: jitterEnabled ? jitterValue : undefined,
        desiredAliveIntervalMinutes: aliveIntervalEnabled ? aliveIntervalValue : undefined,
      });
      onClose();
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
        aria-labelledby="agent-bulk-settings-title"
        onClick={(event) => event.stopPropagation()}
      >
        <p id="agent-bulk-settings-title" className="dialog-title">
          {t('agents.bulkSettings.title', { count: hostnames.length })}
        </p>
        <p className="field-hint">{t('agents.bulkSettings.hint')}</p>

        <label>
          <input type="checkbox" checked={logLevelEnabled} onChange={(e) => setLogLevelEnabled(e.target.checked)} />
          {t('agentDetail.pushedLogLevel')}
        </label>
        <select value={logLevel} disabled={!logLevelEnabled} onChange={(e) => setLogLevel(e.target.value)}>
          {LOG_LEVELS.map((level) => (
            <option key={level} value={level}>
              {level}
            </option>
          ))}
        </select>

        <label>
          <input type="checkbox" checked={intervalEnabled} onChange={(e) => setIntervalEnabled(e.target.checked)} />
          {t('agentDetail.pushedUpdateCheckIntervalMinutes')}
        </label>
        <input
          type="number"
          min={MIN_INTERVAL_MINUTES}
          max={MAX_INTERVAL_MINUTES}
          disabled={!intervalEnabled}
          value={intervalMinutes}
          onChange={(e) => setIntervalMinutes(e.target.value)}
        />

        <label>
          <input type="checkbox" checked={jitterEnabled} onChange={(e) => setJitterEnabled(e.target.checked)} />
          {t('agentDetail.pushedUpdateCheckJitterSeconds')}
        </label>
        <input
          type="number"
          min={MIN_JITTER_SECONDS}
          max={MAX_JITTER_SECONDS}
          disabled={!jitterEnabled}
          value={jitterSeconds}
          onChange={(e) => setJitterSeconds(e.target.value)}
        />

        <label>
          <input type="checkbox" checked={aliveIntervalEnabled} onChange={(e) => setAliveIntervalEnabled(e.target.checked)} />
          {t('agentDetail.pushedAliveIntervalMinutes')}
        </label>
        <input
          type="number"
          min={MIN_ALIVE_INTERVAL_MINUTES}
          max={MAX_ALIVE_INTERVAL_MINUTES}
          disabled={!aliveIntervalEnabled}
          value={aliveIntervalMinutes}
          onChange={(e) => setAliveIntervalMinutes(e.target.value)}
        />

        {error && (
          <p role="alert" className="login-error">
            {error}
          </p>
        )}

        <div className="dialog-actions">
          <button
            type="button"
            className="btn-accent"
            disabled={saving || !anyFieldEnabled || !intervalValid || !jitterValid || !aliveIntervalValid}
            onClick={() => void save()}
          >
            {t('agents.bulkSettings.save')}
          </button>
          <button type="button" onClick={onClose}>
            {t('agentDetail.close')}
          </button>
        </div>
      </div>
    </div>
  );
}
