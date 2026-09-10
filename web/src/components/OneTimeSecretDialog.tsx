import { useEffect, useState } from 'react';

/**
 * Displays a secret that is shown exactly once and never retrievable again
 * — first used for the admin-mediated certificate re-issuance token
 * (updatewatch2-server#8). A real overlay dialog (backdrop + centered box,
 * Escape or a backdrop click closes it) — no library, just a fixed-position
 * div; this app has no other modal to share code with yet, and Nocturne's
 * own dialog pattern (`.dialog-backdrop`/`.dialog`) is plain CSS on plain
 * HTML, so there's nothing a library would add here. Not a focus trap —
 * for a single-admin internal tool showing one short-lived token, that's
 * an acceptable simplification, not an oversight.
 */
export function OneTimeSecretDialog({
  label,
  body,
  value,
  copyLabel,
  copiedLabel,
  closeLabel,
  onClose,
}: {
  label: string;
  body: string;
  value: string;
  copyLabel: string;
  copiedLabel: string;
  closeLabel: string;
  onClose: () => void;
}) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  const copy = () => {
    void navigator.clipboard
      .writeText(value)
      .then(() => setCopied(true))
      .catch(() => {
        // Clipboard access can be denied/unavailable (permissions, non-secure
        // context) — the token is still visible and selectable in the
        // <code> below, so there's a fallback even if this silently fails.
      });
  };

  return (
    <div className="dialog-backdrop" onClick={onClose}>
      <div
        className="dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="one-time-secret-title"
        onClick={(event) => event.stopPropagation()}
      >
        <p id="one-time-secret-title" className="dialog-title">
          {label}
        </p>
        <div className="dialog-body">
          {body}
          <code className="dialog-token">{value}</code>
        </div>
        <div className="dialog-actions">
          <button type="button" onClick={copy}>
            {copied ? copiedLabel : copyLabel}
          </button>
          <button type="button" onClick={onClose}>
            {closeLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
