namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// A managed endpoint (Windows today, Linux planned). Identified uniquely by
/// <see cref="Hostname"/> — see CLAUDE.md "Agents are identified by hostname".
/// </summary>
public class Agent
{
    public int Id { get; set; }

    public required string Hostname { get; set; }

    public string? DnsName { get; set; }

    public string? OperatingSystem { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Version string reported by the agent itself (independent of protocol/server versions).</summary>
    public string? AgentVersion { get; set; }

    /// <summary>
    /// SHA-256 thumbprint of the client certificate issued to this agent after
    /// approval. Also doubles as the one-shot-delivery marker for
    /// <see cref="RegistrationTokenHash"/>'s flow: once set, the certificate has
    /// already been handed to the agent and will never be re-issued/re-sent —
    /// see AgentRegistrationService.
    /// </summary>
    public string? ClientCertificateThumbprint { get; set; }

    /// <summary>
    /// SHA-1 thumbprint of the same certificate as <see cref="ClientCertificateThumbprint"/>
    /// — display-only, alongside it in the admin UI, for an admin comparing
    /// against a local tool that shows SHA-1 by default (Windows Certificate
    /// Manager, PowerShell's <c>.Thumbprint</c>, <c>certutil</c>, `openssl
    /// x509 -fingerprint` without `-sha256`). Never used for any lookup or
    /// comparison — <see cref="Certificates.CertificateValidator"/> and every
    /// other internal check still use SHA-256 exclusively.
    /// </summary>
    public string? ClientCertificateThumbprintSha1 { get; set; }

    /// <summary>When the client certificate identified by <see cref="ClientCertificateThumbprint"/> was issued.</summary>
    public DateTimeOffset? ClientCertificateIssuedAt { get; set; }

    /// <summary>When the client certificate identified by <see cref="ClientCertificateThumbprint"/> expires.</summary>
    public DateTimeOffset? ClientCertificateExpiresAt { get; set; }

    /// <summary>
    /// SHA-256 thumbprint of the CA root that signed <see cref="ClientCertificateThumbprint"/>
    /// — captured at issuance/renewal time (see <see cref="Certificates.IssuedCertificate.IssuingRootThumbprintSha256"/>).
    /// Null for a certificate issued before this field existed (an unknown,
    /// not-verifiable value, deliberately never backfilled by guessing) as
    /// well as for an agent with no certificate at all. Lets
    /// <c>AgentRegistrationService.RecordAliveAsync</c> tell whether an
    /// agent's leaf still chains to the CA's CURRENT root — CA root
    /// rotation (updatewatch2-server#6) never reissues an already-onboarded
    /// agent's own leaf on its own, so without this there would be no way
    /// to know which agents are still relying on a since-superseded root.
    /// </summary>
    public string? IssuingRootThumbprint { get; set; }

    /// <summary>
    /// SHA-256 hash of the opaque registration token handed to this agent on
    /// first contact (never the raw token — same secret-hygiene convention as
    /// password/AD-bind-password storage elsewhere in this codebase). Used to
    /// prevent a different host from hijacking an in-flight, not-yet-approved
    /// registration for the same hostname. Not needed once a certificate has
    /// been delivered, so it's cleared at that point.
    /// </summary>
    public string? RegistrationTokenHash { get; set; }

    /// <summary>
    /// False until an admin manually confirms this agent (individually or via bulk approval).
    /// No client certificate is issued, and no authenticated traffic is accepted, before approval.
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>Set from the agent's most recent update-check report; independent of update installation.</summary>
    public bool RebootRequired { get; set; }

    /// <summary>
    /// Raw count of the agent's most recently reported updates, set by
    /// <see cref="Updates.UpdateService.ReportUpdatesAsync"/> — NOT what the
    /// admin UI displays. The displayed pending-update count is computed
    /// live by <see cref="Agents.AgentService"/>, excluding anything an
    /// active <see cref="UpdateFilter"/> matches, so it stays correct
    /// immediately after a filter is added/edited/deleted rather than only
    /// after this agent's next report.
    /// </summary>
    public int PendingUpdateCount { get; set; }

    /// <summary>
    /// Set when an admin triggers a remote install (updatewatch2-server#10);
    /// cleared once the agent acknowledges having acted on it (see
    /// <see cref="Updates.IUpdateService.AcknowledgeInstallAsync"/>). Delivery
    /// is poll-based — surfaced to the agent as part of its regular alive
    /// heartbeat response, not pushed.
    /// </summary>
    public DateTimeOffset? PendingInstallRequestedAt { get; set; }

    /// <summary>
    /// JSON-serialized array of the specific <see cref="UpdateItem.PackageId"/>
    /// values an admin selected when triggering this pending install — an
    /// admin's way to install only some pending updates while sparing
    /// others (<see cref="Updates.IUpdateService.TriggerInstallAsync"/>).
    /// Null means "install everything currently pending", the original
    /// behavior this replaces the sole option of. Not exposed on any
    /// admin-facing DTO — purely internal plumbing between
    /// <c>TriggerInstallAsync</c> (which writes it from the admin-selected
    /// <see cref="UpdateItem"/> ids) and
    /// <c>AgentRegistrationService.RecordAliveAsync</c> (which hands it
    /// back to the agent, whose own next search re-resolves these PackageIds
    /// against whatever it currently finds — this server never tells an
    /// agent to install something it can't independently re-verify is
    /// still actually pending). Cleared alongside <see cref="PendingInstallRequestedAt"/>
    /// once the agent acknowledges.
    /// </summary>
    public string? PendingInstallUpdateIds { get; set; }

    /// <summary>
    /// The <see cref="Updates.InstallOutcome"/> name (e.g. "Succeeded") from
    /// the agent's most recent install acknowledgement — a plain string, not
    /// the enum type itself, matching this codebase's existing convention
    /// for wire/DB-facing simple enums (see e.g. AdminSettings's
    /// SmtpEncryption column).
    /// </summary>
    public string? LastInstallOutcome { get; set; }

    /// <summary>
    /// Human-readable reason for the most recent install acknowledgement,
    /// only ever meaningful (and only ever set) alongside a
    /// <see cref="LastInstallOutcome"/> of "Failed" — the agent's own
    /// OS-level tool output (apt-get/dnf stderr, a Windows Update result
    /// code) or a caught exception's message, capped agent-side before it
    /// ever reaches this column. Added (DB schema 0.15.5) after a real
    /// production install failure took raising the agent's log level and
    /// live-tailing journalctl to even see — before this, an admin had no
    /// way to learn WHY an install failed from the admin UI at all. Reset
    /// to null on a Succeeded ack so a stale error never lingers next to a
    /// since-successful install.
    /// </summary>
    public string? LastInstallErrorDetail { get; set; }

    public DateTimeOffset? LastInstallCompletedAt { get; set; }

    public DateTimeOffset? LastAliveAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
