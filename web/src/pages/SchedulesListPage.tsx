import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { schedulesApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import type { Schedule, UpsertSchedule } from '../api/types';
import { ScheduleDialog } from '../components/ScheduleDialog';
import { ScheduleRunsDialog } from '../components/ScheduleRunsDialog';

// Same "keep an already-rendered list alive across a transient poll
// failure" pattern as AgentsListPage — see its own POLL_INTERVAL_MS
// comment. Schedules change less often than agents, so a longer interval
// is enough here.
const POLL_INTERVAL_MS = 15000;

function actionLabel(schedule: Schedule, t: (key: string) => string): string {
  if (schedule.actionInstall && schedule.actionReboot) {
    return t('schedules.action.installAndReboot');
  }
  if (schedule.actionInstall) {
    return t('schedules.action.install');
  }
  return t('schedules.action.reboot');
}

function typeLabel(schedule: Schedule, t: (key: string) => string): string {
  if (schedule.scheduleType === 'Once') {
    return t('schedules.type.once');
  }
  if (schedule.scheduleType === 'Cron') {
    return t('schedules.type.cron');
  }
  return schedule.pattern === 'Weekly' ? t('schedules.type.weekly') : t('schedules.type.interval');
}

export function SchedulesListPage() {
  const { t, i18n } = useTranslation();
  const [schedules, setSchedules] = useState<Schedule[] | null>(null);
  const [error, setError] = useState(false);
  const [dialogSchedule, setDialogSchedule] = useState<Schedule | 'new' | null>(null);
  const [historyScheduleId, setHistoryScheduleId] = useState<number | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const hasLoadedOnceRef = useRef(false);

  const reload = () => {
    schedulesApi
      .list()
      .then((data) => {
        hasLoadedOnceRef.current = true;
        setSchedules(data);
      })
      .catch(() => {
        if (!hasLoadedOnceRef.current) {
          setError(true);
        }
      });
  };

  useEffect(() => {
    reload();
    const id = setInterval(reload, POLL_INTERVAL_MS);
    return () => clearInterval(id);
  }, []);

  const runNow = async (schedule: Schedule) => {
    setActionError(null);
    try {
      await schedulesApi.runNow(schedule.id);
      reload();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : t('login.genericError'));
    }
  };

  const deleteSchedule = async (schedule: Schedule) => {
    if (!window.confirm(t('schedules.deleteConfirm', { name: schedule.name }))) {
      return;
    }
    setActionError(null);
    try {
      await schedulesApi.delete(schedule.id);
      reload();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : t('login.genericError'));
    }
  };

  const saveSchedule = async (id: number | null, request: UpsertSchedule) => {
    if (id === null) {
      await schedulesApi.create(request);
    } else {
      await schedulesApi.update(id, request);
    }
    setDialogSchedule(null);
    reload();
  };

  if (error) {
    return <p role="alert">{t('schedules.loadError')}</p>;
  }

  if (schedules === null) {
    return <p>{t('schedules.loading')}</p>;
  }

  return (
    <section>
      <h1>{t('schedules.title')}</h1>

      {actionError && (
        <p role="alert" className="login-error">
          {actionError}
        </p>
      )}

      <div className="list-toolbar">
        <span className="text-muted">{t('schedules.count', { count: schedules.length })}</span>
        <button type="button" className="btn-accent" onClick={() => setDialogSchedule('new')}>
          {t('schedules.new')}
        </button>
      </div>

      {schedules.length === 0 ? (
        <p>{t('schedules.empty')}</p>
      ) : (
        <div className="card table-card">
          <table>
            <thead>
              <tr>
                <th>{t('schedules.columns.name')}</th>
                <th>{t('schedules.columns.type')}</th>
                <th>{t('schedules.columns.nextRun')}</th>
                <th>{t('schedules.columns.action')}</th>
                <th>{t('schedules.columns.agents')}</th>
                <th>{t('schedules.columns.status')}</th>
                <th aria-label={t('schedules.columns.actions')} />
              </tr>
            </thead>
            <tbody>
              {schedules.map((schedule) => (
                <tr key={schedule.id}>
                  <td>{schedule.name}</td>
                  <td>{typeLabel(schedule, t)}</td>
                  <td>{schedule.nextRunAt ? new Date(schedule.nextRunAt).toLocaleString(i18n.language) : '—'}</td>
                  <td>{actionLabel(schedule, t)}</td>
                  <td>{schedule.hostnames.length}</td>
                  <td>{t(`schedules.status.${schedule.status}`)}</td>
                  <td>
                    <div className="detail-header-actions">
                      <button type="button" onClick={() => setDialogSchedule(schedule)}>
                        {t('schedules.edit')}
                      </button>
                      <button type="button" onClick={() => setHistoryScheduleId(schedule.id)}>
                        {t('schedules.history')}
                      </button>
                      <button type="button" onClick={() => void runNow(schedule)}>
                        {t('schedules.runNow')}
                      </button>
                      <button type="button" onClick={() => void deleteSchedule(schedule)}>
                        {t('schedules.delete')}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {dialogSchedule && (
        <ScheduleDialog
          schedule={dialogSchedule === 'new' ? null : dialogSchedule}
          onSave={saveSchedule}
          onClose={() => setDialogSchedule(null)}
        />
      )}

      {historyScheduleId !== null && <ScheduleRunsDialog scheduleId={historyScheduleId} onClose={() => setHistoryScheduleId(null)} />}
    </section>
  );
}
