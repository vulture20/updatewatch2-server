/**
 * Marks an agent row whose self-reported AgentVersion is older than the
 * newest agent release the server currently knows about — see
 * AgentListItem.isOutdated, always computed live server-side (never a
 * stored flag), same discipline OfflineIcon's own doc comment describes.
 * An up arrow (an admin's own suggestion, chosen over a down arrow as the
 * software-industry-standard "update available" symbol — app stores and
 * package managers consistently use an upward arrow for this, never a
 * downward one, which reads more as "download" or "demote") in the
 * app's single accent color, since this is informational/actionable, not
 * a warning that needs attention the way a certificate rejection does —
 * WarningTriangleIcon's amber stays reserved for that. Hardcoded hex
 * rather than var()-referenced, matching WarningTriangleIcon/OfflineIcon's
 * own precedent for a small standalone SVG (accent = tokens.css's
 * --color-accent; dark = --color-bg, for contrast against the accent fill).
 */
export function OutdatedIcon({ title }: { title?: string }) {
  return (
    <svg width="19" height="19" viewBox="0 0 256 256" role={title ? 'img' : undefined} aria-hidden={title ? undefined : true}>
      {title && <title>{title}</title>}
      <circle cx="128" cy="128" r="104" fill="#9184d9"></circle>
      <path d="M128 60 L184 132 L146 132 L146 196 L110 196 L110 132 L72 132 Z" fill="#161826"></path>
    </svg>
  );
}
