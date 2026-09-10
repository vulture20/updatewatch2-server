const UNITS: { unit: Intl.RelativeTimeFormatUnit; seconds: number }[] = [
  { unit: 'year', seconds: 31536000 },
  { unit: 'month', seconds: 2592000 },
  { unit: 'day', seconds: 86400 },
  { unit: 'hour', seconds: 3600 },
  { unit: 'minute', seconds: 60 },
];

/**
 * "3 minutes ago" / "vor 3 Minuten", locale-aware via Intl.RelativeTimeFormat
 * — used for the agents overview's "last seen" column. Falls back to
 * "just now"-style phrasing (via the smallest unit at 0) for anything
 * under a minute, matching what an admin actually cares about (roughly
 * how stale is this, not to-the-second precision).
 */
export function formatRelativeTime(iso: string | null, locale: string): string | null {
  if (!iso) {
    return null;
  }
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) {
    return null;
  }
  const seconds = Math.max(0, (Date.now() - then) / 1000);
  const rtf = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });

  for (const { unit, seconds: unitSeconds } of UNITS) {
    if (seconds >= unitSeconds) {
      return rtf.format(-Math.floor(seconds / unitSeconds), unit);
    }
  }
  return rtf.format(0, 'minute');
}
