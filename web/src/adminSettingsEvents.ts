/**
 * A tiny same-tab event so a fresh `PUT /api/admin/settings` result can
 * reach `SmtpWarningBanner` — rendered as a sibling in `App.tsx`, not a
 * child of `AdminPage` — the instant it's saved, instead of the banner
 * only noticing on its next mount (i.e. a manual page reload). No shared
 * state/context exists in this app for admin settings today; a plain
 * `window` `CustomEvent` is the smallest thing that gets a same-tab
 * component talking to another without introducing one just for this.
 */
const EVENT_NAME = 'uw2:admin-settings-saved';

export interface AdminSettingsSavedDetail {
  smtpConfigured: boolean;
}

export function announceAdminSettingsSaved(detail: AdminSettingsSavedDetail) {
  window.dispatchEvent(new CustomEvent<AdminSettingsSavedDetail>(EVENT_NAME, { detail }));
}

export function onAdminSettingsSaved(listener: (detail: AdminSettingsSavedDetail) => void) {
  const handler = (event: Event) => listener((event as CustomEvent<AdminSettingsSavedDetail>).detail);
  window.addEventListener(EVENT_NAME, handler);
  return () => window.removeEventListener(EVENT_NAME, handler);
}
