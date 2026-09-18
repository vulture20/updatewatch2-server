import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { agentsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import type { AgentListItem, Schedule, SchedulePattern, ScheduleType, UpsertSchedule, WeekdayName } from '../api/types';

const WEEKDAYS: WeekdayName[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

function toDateTimeLocalValue(iso: string | null): string {
  if (!iso) {
    return '';
  }
  const date = new Date(iso);
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function toTimeInputValue(timeOfDay: string): string {
  // "HH:MM:SS" (a serialized TimeSpan) -> "HH:MM" for <input type="time">.
  return timeOfDay.slice(0, 5);
}

/**
 * Create/edit form for a Schedule (updatewatch2-server#25) — a fixed agent
 * list, a firing pattern, and an install/reboot action. Same
 * `.dialog-backdrop`/`.dialog` overlay pattern as AgentSettingsDialog;
 * unlike that dialog this one needs its own agent list (fetched once on
 * open, not shared with the page behind it, since the schedules list page
 * never loads the agent list itself).
 */
export function ScheduleDialog({
  schedule,
  onSave,
  onClose,
}: {
  schedule: Schedule | null;
  onSave: (id: number | null, request: UpsertSchedule) => Promise<void>;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [agents, setAgents] = useState<AgentListItem[] | null>(null);
  const [agentSearch, setAgentSearch] = useState('');

  const [name, setName] = useState(schedule?.name ?? '');
  const [enabled, setEnabled] = useState(schedule?.enabled ?? true);
  const [selectedHostnames, setSelectedHostnames] = useState<Set<string>>(new Set(schedule?.hostnames ?? []));
  const [scheduleType, setScheduleType] = useState<ScheduleType>(schedule?.scheduleType ?? 'Once');
  const [pattern, setPattern] = useState<SchedulePattern>(schedule?.pattern ?? 'Weekly');
  const [onceAt, setOnceAt] = useState(toDateTimeLocalValue(schedule?.onceAt ?? null));
  const [weeklyDays, setWeeklyDays] = useState<Set<WeekdayName>>(new Set(schedule?.weeklyDays ?? []));
  const [timeOfDay, setTimeOfDay] = useState(schedule ? toTimeInputValue(schedule.timeOfDay) : '02:00');
  const [intervalDays, setIntervalDays] = useState((schedule?.intervalDays ?? 1).toString());
  const [intervalStartDate, setIntervalStartDate] = useState(schedule?.intervalStartDate ?? new Date().toISOString().slice(0, 10));
  const [actionInstall, setActionInstall] = useState(schedule?.actionInstall ?? true);
  const [actionReboot, setActionReboot] = useState(schedule?.actionReboot ?? false);
  const [rebootOnlyIfRequired, setRebootOnlyIfRequired] = useState(schedule?.rebootOnlyIfRequired ?? true);
  const [deadlineHours, setDeadlineHours] = useState((schedule?.deadlineHours ?? 4).toString());

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    agentsApi.list().then(setAgents).catch(() => setAgents([]));
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  const filteredAgents = useMemo(() => {
    if (!agents) {
      return [];
    }
    const term = agentSearch.trim().toLowerCase();
    return term ? agents.filter((a) => a.hostname.toLowerCase().includes(term)) : agents;
  }, [agents, agentSearch]);

  const toggleAgent = (hostname: string) => {
    setSelectedHostnames((prev) => {
      const next = new Set(prev);
      if (next.has(hostname)) {
        next.delete(hostname);
      } else {
        next.add(hostname);
      }
      return next;
    });
  };

  const toggleWeekday = (day: WeekdayName) => {
    setWeeklyDays((prev) => {
      const next = new Set(prev);
      if (next.has(day)) {
        next.delete(day);
      } else {
        next.add(day);
      }
      return next;
    });
  };

  const deadlineValue = Number(deadlineHours);
  const intervalValue = Number(intervalDays);
  const deadlineValid = deadlineHours !== '' && Number.isInteger(deadlineValue) && deadlineValue >= 1;
  const intervalValid = scheduleType !== 'Recurring' || pattern !== 'IntervalDays' || (intervalDays !== '' && Number.isInteger(intervalValue) && intervalValue >= 1);
  const formValid =
    name.trim().length > 0 &&
    selectedHostnames.size > 0 &&
    (actionInstall || actionReboot) &&
    deadlineValid &&
    intervalValid &&
    (scheduleType !== 'Once' || onceAt !== '') &&
    (scheduleType !== 'Recurring' || pattern !== 'Weekly' || weeklyDays.size > 0);

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      const request: UpsertSchedule = {
        name,
        enabled,
        scheduleType,
        pattern: scheduleType === 'Recurring' ? pattern : null,
        onceAt: scheduleType === 'Once' && onceAt ? new Date(onceAt).toISOString() : null,
        weeklyDays: scheduleType === 'Recurring' && pattern === 'Weekly' ? [...weeklyDays] : null,
        timeOfDay: `${timeOfDay}:00`,
        intervalDays: scheduleType === 'Recurring' && pattern === 'IntervalDays' ? intervalValue : null,
        intervalStartDate: scheduleType === 'Recurring' && pattern === 'IntervalDays' ? intervalStartDate : null,
        actionInstall,
        actionReboot,
        rebootOnlyIfRequired,
        deadlineHours: deadlineValue,
        hostnames: [...selectedHostnames],
      };
      await onSave(schedule?.id ?? null, request);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('login.genericError'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="dialog-backdrop" onClick={onClose}>
      <div className="dialog" role="dialog" aria-modal="true" aria-labelledby="schedule-dialog-title" onClick={(event) => event.stopPropagation()}>
        <p id="schedule-dialog-title" className="dialog-title">
          {schedule ? t('schedules.dialog.editTitle') : t('schedules.dialog.createTitle')}
        </p>

        <label>
          {t('schedules.dialog.name')}
          <input type="text" value={name} onChange={(e) => setName(e.target.value)} />
        </label>

        <label>
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
          {t('schedules.dialog.enabled')}
        </label>

        <fieldset>
          <legend>{t('schedules.dialog.scheduleType')}</legend>
          <label>
            <input type="radio" name="scheduleType" checked={scheduleType === 'Once'} onChange={() => setScheduleType('Once')} />
            {t('schedules.type.once')}
          </label>
          <label>
            <input type="radio" name="scheduleType" checked={scheduleType === 'Recurring'} onChange={() => setScheduleType('Recurring')} />
            {t('schedules.type.recurring')}
          </label>
        </fieldset>

        {scheduleType === 'Once' ? (
          <label>
            {t('schedules.dialog.onceAt')}
            <input type="datetime-local" value={onceAt} onChange={(e) => setOnceAt(e.target.value)} />
          </label>
        ) : (
          <>
            <fieldset>
              <legend>{t('schedules.dialog.pattern')}</legend>
              <label>
                <input type="radio" name="pattern" checked={pattern === 'Weekly'} onChange={() => setPattern('Weekly')} />
                {t('schedules.type.weekly')}
              </label>
              <label>
                <input type="radio" name="pattern" checked={pattern === 'IntervalDays'} onChange={() => setPattern('IntervalDays')} />
                {t('schedules.type.interval')}
              </label>
            </fieldset>

            {pattern === 'Weekly' ? (
              <fieldset>
                <legend>{t('schedules.dialog.weeklyDays')}</legend>
                {WEEKDAYS.map((day) => (
                  <label key={day}>
                    <input type="checkbox" checked={weeklyDays.has(day)} onChange={() => toggleWeekday(day)} />
                    {t(`schedules.weekday.${day}`)}
                  </label>
                ))}
              </fieldset>
            ) : (
              <>
                <label>
                  {t('schedules.dialog.intervalDays')}
                  <input type="number" min={1} value={intervalDays} onChange={(e) => setIntervalDays(e.target.value)} />
                </label>
                <label>
                  {t('schedules.dialog.intervalStartDate')}
                  <input type="date" value={intervalStartDate} onChange={(e) => setIntervalStartDate(e.target.value)} />
                </label>
              </>
            )}

            <label>
              {t('schedules.dialog.timeOfDay')}
              <input type="time" value={timeOfDay} onChange={(e) => setTimeOfDay(e.target.value)} />
            </label>
          </>
        )}

        <fieldset>
          <legend>{t('schedules.dialog.action')}</legend>
          <label>
            <input type="checkbox" checked={actionInstall} onChange={(e) => setActionInstall(e.target.checked)} />
            {t('schedules.action.install')}
          </label>
          <label>
            <input type="checkbox" checked={actionReboot} onChange={(e) => setActionReboot(e.target.checked)} />
            {t('schedules.action.reboot')}
          </label>
          {actionReboot && (
            <label>
              <input type="checkbox" checked={rebootOnlyIfRequired} onChange={(e) => setRebootOnlyIfRequired(e.target.checked)} />
              {t('schedules.dialog.rebootOnlyIfRequired')}
            </label>
          )}
        </fieldset>

        <label>
          {t('schedules.dialog.deadlineHours')}
          <input type="number" min={1} value={deadlineHours} onChange={(e) => setDeadlineHours(e.target.value)} />
          <p className="field-hint">{t('schedules.dialog.deadlineHoursHint')}</p>
        </label>

        <label>
          {t('schedules.dialog.agents')}
          <input
            type="text"
            value={agentSearch}
            onChange={(e) => setAgentSearch(e.target.value)}
            placeholder={t('schedules.dialog.agentsSearchPlaceholder')}
          />
        </label>
        <div className="card" style={{ maxHeight: 180, overflowY: 'auto' }}>
          {agents === null ? (
            <p>{t('schedules.loading')}</p>
          ) : (
            filteredAgents.map((agent) => (
              <label key={agent.hostname} style={{ display: 'block' }}>
                <input type="checkbox" checked={selectedHostnames.has(agent.hostname)} onChange={() => toggleAgent(agent.hostname)} />
                {agent.hostname}
              </label>
            ))
          )}
        </div>
        <p className="field-hint">{t('schedules.dialog.agentsSelectedCount', { count: selectedHostnames.size })}</p>

        {error && (
          <p role="alert" className="login-error">
            {error}
          </p>
        )}

        <div className="dialog-actions">
          <button type="button" className="btn-accent" disabled={saving || !formValid} onClick={() => void save()}>
            {t('schedules.dialog.save')}
          </button>
          <button type="button" onClick={onClose}>
            {t('schedules.dialog.cancel')}
          </button>
        </div>
      </div>
    </div>
  );
}
