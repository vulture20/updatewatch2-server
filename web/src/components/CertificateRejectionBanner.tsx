import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { certificateRejectionsApi } from '../api/endpoints';

// Certificate rejections are high-priority/security-relevant (CLAUDE.md) —
// polled rather than fetched once like SmtpWarningBanner, so a rejection
// that starts happening while an admin is already logged in and looking at
// another page still becomes visible without a manual reload. Not as tight
// as AgentsListPage/AgentDetailPage's 5s (that's about watching one agent's
// onboarding settle in real time); this is "notice within a reasonable
// time", not "watch it happen live".
const POLL_INTERVAL_MS = 15000;

/**
 * Red warning shown to logged-in admins whenever an agent has presented an
 * invalid or expired client certificate within the last 24 hours (server-
 * side: CertificateRejectionService's own lookback window) — immediately
 * visible in the UI per CLAUDE.md's requirement, alongside the Warning-level
 * application log line and audit log entry the server also always writes.
 * No dismiss/acknowledge action, same as SmtpWarningBanner: it just reflects
 * current state and clears itself once nothing recent remains.
 */
export function CertificateRejectionBanner() {
  const { t } = useTranslation();
  const [count, setCount] = useState(0);

  useEffect(() => {
    let cancelled = false;
    const load = () => {
      certificateRejectionsApi
        .getStatus()
        .then((status) => {
          if (!cancelled) {
            setCount(status.recentCount);
          }
        })
        .catch(() => {
          // Couldn't load (e.g. no admin session yet) — say nothing rather
          // than showing a misleading warning, same as SmtpWarningBanner.
        });
    };
    load();
    const id = setInterval(load, POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);

  if (count === 0) {
    return null;
  }

  return (
    <div role="alert" className="smtp-warning">
      {t('certificateRejections.banner', { count })}
    </div>
  );
}
