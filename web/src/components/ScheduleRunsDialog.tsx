import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { schedulesApi } from '../api/endpoints';
import type { ScheduleRun } from '../api/types';

/** Read-only run history for one Schedule (updatewatch2-server#25) — same `.dialog-backdrop`/`.dialog` overlay pattern as the other two dialogs in this app. */
export function ScheduleRunsDialog({ scheduleId, onClose }: { scheduleId: number; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const [runs, setRuns] = useState<ScheduleRun[] | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    schedulesApi
      .getRuns(scheduleId)
      .then(setRuns)
      .catch(() => setError(true));
  }, [scheduleId]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  return (
    <div className="dialog-backdrop" onClick={onClose}>
      <div className="dialog" role="dialog" aria-modal="true" aria-labelledby="schedule-runs-title" onClick={(event) => event.stopPropagation()}>
        <p id="schedule-runs-title" className="dialog-title">
          {t('schedules.runs.title')}
        </p>

        {error && <p role="alert">{t('schedules.runs.loadError')}</p>}
        {!error && runs === null && <p>{t('schedules.loading')}</p>}
        {!error && runs !== null && runs.length === 0 && <p>{t('schedules.runs.empty')}</p>}

        {runs && runs.length > 0 && (
          <div className="card-stack">
            {runs.map((run) => (
              <div className="card" key={run.id}>
                <p className="card-title">{new Date(run.firedAt).toLocaleString(i18n.language)}</p>
                <table>
                  <thead>
                    <tr>
                      <th>{t('schedules.runs.hostname')}</th>
                      <th>{t('schedules.runs.install')}</th>
                      <th>{t('schedules.runs.reboot')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {run.agents.map((agent) => (
                      <tr key={agent.hostname}>
                        <td>{agent.hostname}</td>
                        <td title={agent.errorDetail ?? undefined}>{t(`schedules.runStatus.${agent.installStatus}`)}</td>
                        <td title={agent.errorDetail ?? undefined}>{t(`schedules.runStatus.${agent.rebootStatus}`)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ))}
          </div>
        )}

        <div className="dialog-actions">
          <button type="button" onClick={onClose}>
            {t('schedules.dialog.cancel')}
          </button>
        </div>
      </div>
    </div>
  );
}
