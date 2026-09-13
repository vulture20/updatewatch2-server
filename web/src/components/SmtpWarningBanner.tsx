import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { onAdminSettingsSaved } from '../adminSettingsEvents';
import { adminApi, notificationsApi } from '../api/endpoints';
import { WarningTriangleIcon } from './WarningTriangleIcon';

// Same cadence CertificateRejectionBanner/AgentUpdateErrorBanner already
// poll at — "notice within a reasonable time", not "watch it happen
// live". Cheap here too: this only ever reads SmtpHealthCheckWorker's own
// cached result (a plain in-memory flag), never triggers a live SMTP
// probe itself — that only happens on the worker's own, much coarser
// 5-minute cadence server-side. See SmtpHealthStatus's doc comment.
const POLL_INTERVAL_MS = 15000;

/**
 * Warning shown to admins when the mail server is unreachable or
 * misconfigured (CLAUDE.md section 6.3) — both halves now, closing
 * updatewatch2-server#12 ("wire the SMTP warning banner to the real
 * reachability check, not just 'is it configured'"). Combines two
 * independent signals rather than one:
 *
 * - `smtpConfigured`, from `/api/admin/settings` (host/from-address
 *   present) — updated instantly by `AdminPage`'s own settings-save event
 *   (`onAdminSettingsSaved`), so fixing an obvious misconfiguration makes
 *   the banner disappear the moment "Save" succeeds, the same snappy
 *   behavior this half already had before this issue (server v0.29.1).
 * - `smtpHealthy`, from the new, separately-polled `GET
 *   /api/admin/notifications/smtp-health` — a server-side *cached* result
 *   (`SmtpHealthCheckWorker`, every 5 minutes), not a live probe on every
 *   read, the explicit trade-off this issue's own "worth deciding" note
 *   called out and the one this codebase chose. Polled here on its own
 *   `POLL_INTERVAL_MS` cadence (independent of the settings-save event)
 *   specifically so a server that goes unreachable *while* an admin is
 *   already logged in and looking at another page still becomes visible
 *   without a manual reload — a plain one-time fetch on mount, like this
 *   component used to do, would never notice that.
 *
 * The two are deliberately NOT collapsed into one combined "is it fine"
 * flag server-side: `smtpConfigured` can react instantly to a save,
 * `smtpHealthy` cannot (it's only as fresh as the worker's own last
 * tick) — showing the warning whenever *either* signal says something's
 * wrong, per CLAUDE.md's exact wording, means a save that fixes an
 * obvious typo still disappears immediately even though the reachability
 * half hasn't been re-checked yet.
 *
 * Neutral, divider-bordered treatment (`.banner-neutral`) — a
 * misconfiguration/outage, not an active security event, unlike
 * CertificateRejectionBanner's accent-tinted styling.
 */
export function SmtpWarningBanner() {
  const { t } = useTranslation();
  const [smtpConfigured, setSmtpConfigured] = useState(true);
  const [smtpHealthy, setSmtpHealthy] = useState(true);

  useEffect(() => {
    let cancelled = false;
    adminApi
      .getSettings()
      .then((settings) => {
        if (!cancelled) {
          setSmtpConfigured(settings.smtpConfigured);
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

  useEffect(() => onAdminSettingsSaved(({ smtpConfigured: configured }) => setSmtpConfigured(configured)), []);

  useEffect(() => {
    const load = () => {
      notificationsApi
        .getSmtpHealth()
        .then((status) => setSmtpHealthy(status.healthy))
        .catch(() => {
          // Same reasoning as the settings fetch above — say nothing
          // rather than showing a misleading warning.
        });
    };

    load();
    const id = setInterval(load, POLL_INTERVAL_MS);
    return () => clearInterval(id);
  }, []);

  if (smtpConfigured && smtpHealthy) {
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
