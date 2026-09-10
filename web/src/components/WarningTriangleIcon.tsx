/**
 * The one warning icon shared by every "something needs an admin's
 * attention" spot in this app (SMTP/certificate-rejection banners, a
 * flagged agent row, the agent detail page's rejection alert) — copied
 * verbatim from the "UpdateWatch2 Redesign" mockup. Colors are
 * deliberately hardcoded, not tokenized: Nocturne itself has no warning
 * color role (see tokens.css's own note on --color-danger), and this
 * specific amber has no equivalent in either theme's ramp.
 */
export function WarningTriangleIcon({ title }: { title?: string }) {
  return (
    <svg width="19" height="19" viewBox="0 0 256 256" role={title ? 'img' : undefined} aria-hidden={title ? undefined : true}>
      {title && <title>{title}</title>}
      <path d="M128 24 L246 216 A12 12 0 0 1 236 234 L20 234 A12 12 0 0 1 10 216 Z" fill="#f5c451" stroke="#8a6a12" strokeWidth="6"></path>
      <rect x="121" y="96" width="14" height="76" rx="6" fill="#3a2c05"></rect>
      <circle cx="128" cy="196" r="9" fill="#3a2c05"></circle>
    </svg>
  );
}
