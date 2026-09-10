import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { onAdminSettingsSaved } from '../adminSettingsEvents';
import { adminApi } from '../api/endpoints';
import { WarningTriangleIcon } from './WarningTriangleIcon';

/**
 * Warning shown to admins when the mail server is unreachable or
 * misconfigured (CLAUDE.md section 6.3). Currently only reflects
 * `smtpConfigured` (host/from-address present) from `/api/admin/settings`
 * — the live reachability check already exists server-side
 * (`IEmailNotificationService.IsHealthyAsync`) but isn't exposed via that
 * endpoint yet. TODO: switch this to the reachability check once it is.
 * Neutral, divider-bordered treatment (`.banner-neutral`) — a
 * misconfiguration, not an active security event, unlike
 * CertificateRejectionBanner's accent-tinted styling.
 *
 * Beyond its initial mount-time fetch, this also listens for
 * `AdminPage`'s own settings save (`onAdminSettingsSaved` — a same-tab
 * `window` event, since this banner and `AdminPage` are siblings under
 * `App.tsx`, not parent/child) so fixing the SMTP config makes the
 * banner disappear the moment "Save" succeeds, not only after the next
 * page load — a user report that it otherwise needed a manual F5 after
 * every fix.
 */
export function SmtpWarningBanner() {
  const { t } = useTranslation();
  const [showWarning, setShowWarning] = useState(false);

  useEffect(() => {
    let cancelled = false;
    adminApi
      .getSettings()
      .then((settings) => {
        if (!cancelled) {
          setShowWarning(!settings.smtpConfigured);
        }
      })
      .catch(() => {
        // Settings couldn't be loaded (e.g. no admin session yet) — say
        // nothing rather than showing a misleading warning.
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => onAdminSettingsSaved(({ smtpConfigured }) => setShowWarning(!smtpConfigured)), []);

  if (!showWarning) {
    return null;
  }

  return (
    <div role="alert" className="banner banner-neutral">
      <WarningTriangleIcon />
      <span>{t('login.smtpWarning')}</span>
      <Link to="/admin?tab=notifications">{t('login.smtpWarningLink')}</Link>
    </div>
  );
}
