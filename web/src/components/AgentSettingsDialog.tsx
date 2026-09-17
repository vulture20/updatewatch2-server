import { useEffect } from 'react';

/**
 * Per-agent settings, opened from AgentDetailPage's header via a single
 * "Settings" button (server v1.3.13, at the user's explicit request —
 * "Kannst du in den Agent-Details den Button 'Zertifikat neu ausstellen'
 * durch 'Einstellungen' ersetzen... Hier sollen später auch weitere
 * Einstellungen wie der Alive-Intervall konfigurierbar sein."). Reissue
 * certificate and Delete agent both moved in here from the header's own
 * button row, each with a short explanation — this is meant to grow into
 * the one place an admin configures anything agent-specific (a future
 * per-agent alive-interval override, mentioned by the user, is the next
 * candidate) rather than adding more one-off header buttons. Same
 * `.dialog-backdrop`/`.dialog` overlay pattern as OneTimeSecretDialog
 * (backdrop + centered box, Escape or a backdrop click closes it) — not a
 * shared hook yet, since there are only two dialogs in this app and the
 * Escape-handling effect is a few lines each.
 */
export function AgentSettingsDialog({
  title,
  reissueLabel,
  reissueHint,
  onReissueCertificate,
  deleteLabel,
  deleteHint,
  onDelete,
  closeLabel,
  onClose,
}: {
  title: string;
  reissueLabel: string;
  reissueHint: string;
  // Omitted entirely (not just disabled) for an unapproved agent — it
  // never had a certificate to reissue, matching the header button's own
  // previous approved-only gating.
  onReissueCertificate?: () => void;
  deleteLabel: string;
  deleteHint: string;
  onDelete: () => void;
  closeLabel: string;
  onClose: () => void;
}) {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  return (
    <div className="dialog-backdrop" onClick={onClose}>
      <div
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="agent-settings-title"
        onClick={(event) => event.stopPropagation()}
      >
        <p id="agent-settings-title" className="dialog-title">
          {title}
        </p>

        {onReissueCertificate && (
          <div>
            <button type="button" onClick={onReissueCertificate}>
              {reissueLabel}
            </button>
            <p className="field-hint">{reissueHint}</p>
          </div>
        )}

        <div>
          <button type="button" onClick={onDelete}>
            {deleteLabel}
          </button>
          <p className="field-hint">{deleteHint}</p>
        </div>

        <div className="dialog-actions">
          <button type="button" onClick={onClose}>
            {closeLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
