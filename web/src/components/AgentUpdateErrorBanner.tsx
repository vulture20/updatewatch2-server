import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { agentUpdatesApi } from '../api/endpoints';
import { WarningTriangleIcon } from './WarningTriangleIcon';

// Same reasoning as CertificateRejectionBanner: polled, not fetched once
// like SmtpWarningBanner, so a connectivity/download failure that starts
// happening while an admin is already logged in and looking at another
// page still becomes visible without a manual reload.
const POLL_INTERVAL_MS = 15000;

/**
 * Warning shown to logged-in admins when agent auto-update is enabled but
 * its last GitHub check or asset download failed
 * (AgentUpdateStatusDto.lastError) — no internet access, GitHub rate
 * limits, a bad token, and so on. At the user's explicit request: an error
 * here should only ever surface when automatic updates are actually
 * switched on.
 *
 * Deliberately gated on `enabled`, not just a nonzero `lastError`: a
 * server that has the feature turned off — including one deliberately
 * relying only on the manual-upload escape hatch for an offline
 * deployment (see CLAUDE.md's "Agent auto-update" bullet) — is expected
 * to never successfully reach GitHub, and its periodic background check
 * still runs and still records that failure in `lastError` even while
 * disabled. Showing a banner for that would be permanent noise on exactly
 * the deployments this banner should stay silent on, not a real problem
 * to act on.
 *
 * Neutral, divider-bordered treatment (`.banner-neutral`) like
 * `SmtpWarningBanner` — a connectivity/configuration issue, not an active
 * security event, unlike `CertificateRejectionBanner`'s accent styling.
 */
export function AgentUpdateErrorBanner() {
  const { t } = useTranslation();
  const [lastError, setLastError] = useState<string | null>(null);

  const load = () => {
    agentUpdatesApi
      .getStatus()
      .then((status) => setLastError(status.enabled ? status.lastError : null))
      .catch(() => {
        // Couldn't load (e.g. no admin session yet) — say nothing rather
        // than showing a misleading warning, same as the other banners.
      });
  };

  useEffect(() => {
    load();
    const id = setInterval(load, POLL_INTERVAL_MS);
    return () => clearInterval(id);
  }, []);

  if (!lastError) {
    return null;
  }

  return (
    <div role="alert" className="banner banner-neutral">
      <WarningTriangleIcon />
      <span>{t('agentUpdateError.banner', { error: lastError })}</span>
      <Link to="/admin?tab=general">{t('agentUpdateError.link')}</Link>
    </div>
  );
}
