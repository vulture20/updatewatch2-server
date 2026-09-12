const UNITS: { unit: Intl.RelativeTimeFormatUnit; seconds: number }[] = [
  { unit: 'year', seconds: 31536000 },
  { unit: 'month', seconds: 2592000 },
  { unit: 'day', seconds: 86400 },
  { unit: 'hour', seconds: 3600 },
  { unit: 'minute', seconds: 60 },
];

/** How long ago `iso` was, expressed as a whole count in the largest unit that fits (never zero units, floors to 'minute'). Shared by formatRelativeTime and anything else that needs the raw magnitude without Intl.RelativeTimeFormat's "ago"/"in" phrasing baked in. */
export function elapsedSince(iso: string): { value: number; unit: Intl.RelativeTimeFormatUnit } | null {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) {
    return null;
  }
  const seconds = Math.max(0, (Date.now() - then) / 1000);

  for (const { unit, seconds: unitSeconds } of UNITS) {
    if (seconds >= unitSeconds) {
      return { value: Math.floor(seconds / unitSeconds), unit };
    }
  }
  return { value: 0, unit: 'minute' };
}

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
  const elapsed = elapsedSince(iso);
  if (!elapsed) {
    return null;
  }
  const rtf = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
  return rtf.format(-elapsed.value, elapsed.unit);
}
