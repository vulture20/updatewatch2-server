import { useEffect, useState } from 'react';
import { onAdminSettingsSaved } from '../adminSettingsEvents';
import { adminApi } from '../api/endpoints';
import type { AdminSettings } from '../api/types';

// Matches AdminSettingsStore's own default — used only until the real
// setting has loaded, or if it never manages to (e.g. no admin session
// yet), same "say nothing, keep the previous/default value" reasoning as
// SmtpWarningBanner's own settings fetch.
const DEFAULT_ITEMS_PER_PAGE = 50;

type ItemsPerPageKind = 'default' | 'auditLog';

function fieldFor(kind: ItemsPerPageKind): keyof Pick<AdminSettings, 'itemsPerPage' | 'auditLogItemsPerPage'> {
  return kind === 'auditLog' ? 'auditLogItemsPerPage' : 'itemsPerPage';
}

/**
 * The "items per page" setting (Settings → General) driving a paginated
 * list's page size — at the user's explicit request. 0 means unlimited
 * (everything on one page/response). Mirrors SmtpWarningBanner's
 * fetch-once-plus-live-update pattern: fetched once on mount, then kept in
 * sync with a same-tab save via `onAdminSettingsSaved` so a change in
 * Settings takes effect immediately on every already-open list, not just
 * after a reload.
 *
 * Two independent underlying settings share this one hook rather than each
 * getting its own: `AdminSettings.itemsPerPage` (the default, `kind`
 * omitted — used by AgentsListPage/SchedulesListPage) originally also
 * governed the Audit Log, until the user asked for that to be
 * independently configurable — `kind: 'auditLog'` reads
 * `AdminSettings.auditLogItemsPerPage` instead, a genuinely separate value,
 * not an override of the default.
 */
export function useItemsPerPage(kind: ItemsPerPageKind = 'default'): number {
  const [itemsPerPage, setItemsPerPage] = useState(DEFAULT_ITEMS_PER_PAGE);
  const field = fieldFor(kind);

  useEffect(() => {
    let cancelled = false;
    adminApi
      .getSettings()
      .then((settings) => {
        if (!cancelled && typeof settings[field] === 'number') {
          setItemsPerPage(settings[field]);
        }
      })
      .catch(() => {
        // Couldn't load (e.g. no admin session yet) — keep the default.
      });
    return () => {
      cancelled = true;
    };
  }, [field]);

  useEffect(
    () =>
      onAdminSettingsSaved((detail) => {
        if (typeof detail[field] === 'number') {
          setItemsPerPage(detail[field]);
        }
      }),
    [field],
  );

  return itemsPerPage;
}
