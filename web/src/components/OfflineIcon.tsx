/**
 * Marks an agent row/detail page whose last heartbeat is older than the
 * admin-configured offline threshold (Settings → General → Offline
 * detection) — see AgentListItem.isOffline/AgentDetail.isOffline, always
 * computed live server-side, never a stored flag. Deliberately neutral
 * grey, not the amber WarningTriangleIcon uses: "offline" is a status, not
 * a warning that needs attention the way a certificate rejection does, and
 * Nocturne itself has no saturated color role to spend on it (see
 * tokens.css's own "no saturated flood outside the accent" note) — the
 * hex below is --color-neutral-500/--color-neutral-700 from tokens.css,
 * hardcoded rather than var()-referenced to match WarningTriangleIcon's
 * own precedent for a small standalone SVG.
 */
export function OfflineIcon({ title }: { title?: string }) {
  return (
    <svg width="19" height="19" viewBox="0 0 256 256" role={title ? 'img' : undefined} aria-hidden={title ? undefined : true}>
      {title && <title>{title}</title>}
      <circle cx="128" cy="128" r="104" fill="#9397ab"></circle>
      <rect x="48" y="118" width="160" height="20" rx="10" fill="#595d6c" transform="rotate(-45 128 128)"></rect>
    </svg>
  );
}
