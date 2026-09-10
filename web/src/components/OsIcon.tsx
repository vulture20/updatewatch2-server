import tuxIcon from '../assets/tux-icon.png';

/**
 * A small Windows-logo or Linux-Tux icon next to an agent's OS text in
 * the overview table — copied from the "UpdateWatch2 Redesign" mockup.
 * "Windows vs. Linux" is the same `os.includes('Windows')` heuristic used
 * everywhere else this app branches on OS family (there's no OS-family
 * enum on the wire, just AgentListItem.operatingSystem's free-text
 * string). The Tux PNG is dark-on-transparent, so its invert/opacity is
 * theme-dependent — driven by the `.tux-icon` CSS rule (data-theme
 * selector), not a useTheme() branch here, so this stays a plain
 * presentational component with no provider dependency (and no special
 * wrapping needed in tests that don't otherwise touch theming).
 */
export function OsIcon({ operatingSystem }: { operatingSystem: string | null }) {
  if (!operatingSystem) {
    return null;
  }

  if (operatingSystem.includes('Windows')) {
    return (
      <svg width="14" height="14" viewBox="0 0 4875 4875" aria-hidden="true" style={{ flex: 'none' }}>
        <path
          fill="currentColor"
          d="M0 0h2311v2310H0zm2564 0h2311v2310H2564zM0 2564h2311v2311H0zm2564 0h2311v2311H2564"
        ></path>
      </svg>
    );
  }

  return <img src={tuxIcon} alt="" width={16} height={16} className="tux-icon" />;
}
