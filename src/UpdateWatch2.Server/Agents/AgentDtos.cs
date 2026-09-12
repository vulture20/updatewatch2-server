using System.Text.Json.Serialization;

namespace UpdateWatch2.Server.Agents;

/// <summary>Row shape for the main agent overview list.</summary>
public record AgentListItemDto(
    string Hostname,
    bool Approved,
    bool RebootRequired,
    int PendingUpdateCount,
    /// <summary>
    /// Non-null when this agent presented an invalid/expired/unrecognized
    /// client certificate within the last 24 hours (see
    /// <see cref="Certificates.ICertificateRejectionService.GetRecentByHostnameAsync"/>)
    /// — flags the row in the overview list with a warning icon. One of
    /// <see cref="Certificates.CertificateRejectionReason"/>'s values.
    /// </summary>
    string? LastCertificateRejectionReason,
    /// <summary>
    /// Same free-text self-reported string as <see cref="AgentDetailDto.OperatingSystem"/>
    /// (e.g. "Windows Server 2022", "Ubuntu 22.04 LTS") — added so the
    /// overview list can show a per-row OS icon and offer an OS/OS-family
    /// filter without a per-agent round trip. No family enum on the wire;
    /// "Windows vs. Linux" is a client-side `os.includes('Windows')` check,
    /// same as every other OS-family branch in this codebase.
    /// </summary>
    string? OperatingSystem,
    DateTimeOffset? LastAliveAt);

/// <summary>Full shape for the per-agent detail view.</summary>
public record AgentDetailDto(
    string Hostname,
    string? DnsName,
    string? OperatingSystem,
    string? IpAddress,
    string? AgentVersion,
    bool Approved,
    bool RebootRequired,
    int PendingUpdateCount,
    DateTimeOffset? LastAliveAt,
    string? ClientCertificateThumbprint,
    string? ClientCertificateThumbprintSha1,
    DateTimeOffset? ClientCertificateIssuedAt,
    DateTimeOffset? ClientCertificateExpiresAt,
    DateTimeOffset? PendingInstallRequestedAt,
    string? LastInstallOutcome,
    /// <summary>
    /// Only ever non-null alongside a <see cref="LastInstallOutcome"/> of
    /// "Failed" — see <see cref="Db.Entities.Agent.LastInstallErrorDetail"/>'s
    /// own doc comment for why this exists.
    /// </summary>
    string? LastInstallErrorDetail,
    DateTimeOffset? LastInstallCompletedAt,
    DateTimeOffset? PendingRestartRequestedAt,
    string? LastRestartOutcome,
    /// <summary>Only ever non-null alongside a <see cref="LastRestartOutcome"/> of "Failed" — mirrors <see cref="LastInstallErrorDetail"/>.</summary>
    string? LastRestartErrorDetail,
    DateTimeOffset? LastRestartCompletedAt,
    /// <summary>
    /// SHA-256 thumbprint of the internal CA root that actually signed this
    /// agent's current client certificate (<see cref="Db.Entities.Agent.IssuingRootThumbprint"/>)
    /// — compare against Administration → Certificates' current/previous
    /// root thumbprints to see whether this agent has renewed past a CA
    /// root rotation yet. Null for a certificate issued before this was
    /// tracked (updatewatch2-server#6 follow-up) or for an agent with no
    /// certificate at all.
    /// </summary>
    string? IssuingRootThumbprint,
    /// <summary>Same as <see cref="AgentListItemDto.LastCertificateRejectionReason"/> — the reason, if known, shown on the detail page.</summary>
    string? LastCertificateRejectionReason,
    DateTimeOffset? LastCertificateRejectionAt);

/// <summary>
/// How many/which agents would stop authenticating if the CA's previous
/// root were retired right now (updatewatch2-server#6 follow-up) —
/// composed by <see cref="Api.Controllers.CertificateAuthorityController"/>
/// alongside <see cref="Certificates.ICertificateAuthority.GetRotationStatus"/>
/// so an admin sees a real number/list before clicking "Retire Previous
/// Root", not just generic warning text. <see cref="StillOnPreviousRootHostnames"/>
/// is capped (see <see cref="AgentService.GetCaRotationImpactAsync"/>) —
/// <see cref="StillOnPreviousRootCount"/> is always the true total, even
/// when the list itself is truncated. <see cref="UnknownRootAgentCount"/>
/// counts agents with a certificate but no recorded issuing root (issued
/// before <c>Agent.IssuingRootThumbprint</c> existed) — these can't be
/// confirmed either way, so they're surfaced separately rather than folded
/// into (or silently dropped from) the confirmed count.
/// </summary>
public record CaRotationImpactDto(int StillOnPreviousRootCount, IReadOnlyList<string> StillOnPreviousRootHostnames, int UnknownRootAgentCount)
{
    public static readonly CaRotationImpactDto None = new(0, [], 0);
}

public record BulkApproveRequest(IReadOnlyList<string> Hostnames);

public record BulkApproveResult(int ApprovedCount, IReadOnlyList<string> NotFoundHostnames);

/// <summary>
/// Result of an admin-initiated certificate re-issuance (updatewatch2-server#8).
/// <see cref="RegistrationToken"/> is the raw, one-shot registration token —
/// returned exactly once here, never persisted or retrievable again — for
/// the admin to place into the affected agent's local configuration.
/// </summary>
public record ReissueCertificateResult(bool Success, string? RegistrationToken, string? FailureReason)
{
    public static ReissueCertificateResult Failed(string reason) => new(false, null, reason);

    public static ReissueCertificateResult Succeeded(string registrationToken) => new(true, registrationToken, null);
}

/// <summary>
/// How a remote-triggered agent-service restart went, as self-reported by
/// the agent once it has acted on the request. Mirrors
/// <c>Updates.InstallOutcome</c> field-for-field, including the same
/// <see cref="JsonStringEnumConverter"/> requirement — this project has no
/// global one configured, so without this attribute a wire body like
/// <c>{"outcome":"Succeeded"}</c> 400s against the default numeric
/// model-binding <c>Updates.InstallOutcome</c>'s own doc comment already
/// confirmed live for that sibling enum. Kept as its own separate type
/// rather than reusing <c>Updates.InstallOutcome</c> even though the shape
/// is identical — a restart is a distinct agent-lifecycle action, not an
/// OS-update install, and the two are deliberately never conflated
/// (CLAUDE.md's "agent self-update is a separate mechanism... not to be
/// conflated with" rule applies by the same reasoning here).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestartOutcome
{
    Succeeded,
    Failed,
}

/// <summary>
/// Body of <c>POST /api/agents/{hostname}/restart-ack</c> — the agent's
/// acknowledgement that it acted on a pending restart request.
/// <see cref="ErrorDetail"/> is only ever meaningful alongside
/// <see cref="RestartOutcome.Failed"/> (e.g. the platform-specific restart
/// mechanism itself failed to launch) — mirrors <c>Updates.InstallAckRequest</c>.
/// </summary>
public record RestartAckRequest(RestartOutcome Outcome, string? ErrorDetail = null);
