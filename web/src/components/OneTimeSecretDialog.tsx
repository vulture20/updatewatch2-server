import { useState } from 'react';

/**
 * Displays a secret that is shown exactly once and never retrievable again
 * — first used for the admin-mediated certificate re-issuance token
 * (updatewatch2-server#8). No modal/dialog library is in use anywhere in
 * this app, so this is a plain in-flow banner (role="alert", matching
 * SmtpWarningBanner's style) rather than a portal/focus-trap component.
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
    <div role="alert" className="one-time-secret">
      <p className="one-time-secret-label">{label}</p>
      <p>{body}</p>
      <code>{value}</code>
      <div className="one-time-secret-actions">
        <button type="button" onClick={copy}>
          {copied ? copiedLabel : copyLabel}
        </button>
        <button type="button" onClick={onClose}>
          {closeLabel}
        </button>
      </div>
    </div>
  );
}
