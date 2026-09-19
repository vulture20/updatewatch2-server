import { useEffect, useState } from 'react';
import { onAdminSettingsSaved } from '../adminSettingsEvents';
import { adminApi } from '../api/endpoints';

// Matches AdminSettingsStore's own default — used only until the real
// setting has loaded, or if it never manages to (e.g. no admin session
// yet), same "say nothing, keep the previous/default value" reasoning as
// SmtpWarningBanner's own settings fetch.
const DEFAULT_ITEMS_PER_PAGE = 50;

/**
 * The one global "items per page" setting (Settings → General) shared by
 * every paginated list (AuditLogTab, AgentsListPage, SchedulesListPage) —
 * at the user's explicit request. 0 means unlimited (everything on one
 * page/response). Mirrors SmtpWarningBanner's fetch-once-plus-live-update
 * pattern: fetched once on mount, then kept in sync with a same-tab save
 * via `onAdminSettingsSaved` so a change in Settings takes effect
 * immediately on every already-open list, not just after a reload.
 */
export function useItemsPerPage(): number {
  const [itemsPerPage, setItemsPerPage] = useState(DEFAULT_ITEMS_PER_PAGE);

  useEffect(() => {
    let cancelled = false;
    adminApi
      .getSettings()
      .then((settings) => {
        if (!cancelled && typeof settings.itemsPerPage === 'number') {
          setItemsPerPage(settings.itemsPerPage);
        }
      })
      .catch(() => {
        // Couldn't load (e.g. no admin session yet) — keep the default.
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(
    () =>
      onAdminSettingsSaved((detail) => {
        if (typeof detail.itemsPerPage === 'number') {
          setItemsPerPage(detail.itemsPerPage);
        }
      }),
    [],
  );

  return itemsPerPage;
}
