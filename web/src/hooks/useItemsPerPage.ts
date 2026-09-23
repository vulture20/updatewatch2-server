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
export interface ItemsPerPageState {
  itemsPerPage: number;
  /** False until the initial `getSettings()` call has settled (success or failure). */
  loaded: boolean;
}

/**
 * The full state behind {@link useItemsPerPage}, additionally exposing
 * whether `itemsPerPage` is still the placeholder default or a value that's
 * actually been confirmed (either the real setting, or the default
 * confirmed-as-correct after a failed fetch). Use this instead of the plain
 * `useItemsPerPage` when consuming the value triggers a network call of its
 * own (AuditLogTab's server-side page fetch) — waiting for `loaded` avoids
 * firing that call once with the placeholder default and again moments
 * later with the real value. A purely client-side consumer slicing an
 * already-loaded array (AgentsListPage/SchedulesListPage's usePageSlice)
 * has no such cost and can keep using the plain number.
 */
export function useItemsPerPageState(kind: ItemsPerPageKind = 'default'): ItemsPerPageState {
  const [itemsPerPage, setItemsPerPage] = useState(DEFAULT_ITEMS_PER_PAGE);
  const [loaded, setLoaded] = useState(false);
  const field = fieldFor(kind);

  useEffect(() => {
    let cancelled = false;
    adminApi
      .getSettings()
      .then((settings) => {
        if (!cancelled) {
          if (typeof settings[field] === 'number') {
            setItemsPerPage(settings[field]);
          }
          setLoaded(true);
        }
      })
      .catch(() => {
        // Couldn't load (e.g. no admin session yet) — keep the default, but
        // still unblock a consumer waiting on `loaded`.
        if (!cancelled) {
          setLoaded(true);
        }
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

  return { itemsPerPage, loaded };
}

export function useItemsPerPage(kind: ItemsPerPageKind = 'default'): number {
  return useItemsPerPageState(kind).itemsPerPage;
}
