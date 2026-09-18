using System.Text.Json.Serialization;
using UpdateWatch2.Server.Api;

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
    DateTimeOffset? LastAliveAt,
    /// <summary>
    /// True when this agent hasn't heartbeated within the admin-configured
    /// <see cref="AgentOfflineOptions.ThresholdMinutes"/> (or has never
    /// heartbeated at all) — computed live against the current threshold
    /// on every request, per <see cref="AgentOfflineOptions"/>'s own doc
    /// comment, not from a periodically-updated stored flag. Flags the row
    /// with a small icon in the overview list.
    /// </summary>
    bool IsOffline,
    /// <summary>
    /// Same as <see cref="AgentDetailDto.PendingInstallRequestedAt"/> —
    /// added here (server v1.3.4, at the user's explicit request) so the
    /// overview list can show a per-row "Updates" activity badge for an
    /// approved agent without a per-agent round trip, replacing the
    /// previous unconditional (and, once approved, redundant) "Approved"
    /// badge.
    /// </summary>
    DateTimeOffset? PendingInstallRequestedAt,
    /// <summary>Same as <see cref="PendingInstallRequestedAt"/>, for the "Neustart"/reboot activity badge — mirrors <see cref="AgentDetailDto.PendingRebootRequestedAt"/>.</summary>
    DateTimeOffset? PendingRebootRequestedAt,
    /// <summary>Same self-reported string as <see cref="AgentDetailDto.AgentVersion"/> — added alongside <see cref="IsOutdated"/> so the overview list's outdated-agent icon has a version to show in its tooltip.</summary>
    string? AgentVersion,
    /// <summary>
    /// True when <see cref="AgentVersion"/> is older than the newest agent
    /// release the server currently knows about
    /// (<see cref="Db.Entities.AgentUpdateState.LatestVersion"/> —
    /// see <see cref="AgentUpdates.AgentVersionComparer"/> for the shared
    /// comparison this also drives the self-update offer with). Computed
    /// live on every request, the same "never a stored/stale flag"
    /// discipline <see cref="IsOffline"/> already follows — independent of
    /// whether agent auto-update itself is enabled, since this is purely
    /// informational. False whenever no release has ever been recorded, or
    /// either version fails to parse.
    /// </summary>
    bool IsOutdated);

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
    DateTimeOffset? PendingRebootRequestedAt,
    string? LastRebootOutcome,
    /// <summary>Only ever non-null alongside a <see cref="LastRebootOutcome"/> of "Failed" — mirrors <see cref="LastInstallErrorDetail"/>.</summary>
    string? LastRebootErrorDetail,
    DateTimeOffset? LastRebootCompletedAt,
    /// <summary>
    /// When the agent's own machine last booted, self-reported every
    /// heartbeat — see <see cref="Db.Entities.Agent.BootTimeUtc"/>'s own
    /// doc comment. What actually lets an admin confirm a triggered reboot
    /// took effect, as opposed to <see cref="LastRebootOutcome"/>, which
    /// only reflects whether the reboot command was scheduled successfully.
    /// </summary>
    DateTimeOffset? BootTimeUtc,
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
    DateTimeOffset? LastCertificateRejectionAt,
    /// <summary>Same as <see cref="AgentListItemDto.IsOffline"/> — shown next to the hostname on the detail page's Identity card.</summary>
    bool IsOffline,
    /// <summary>Same as <see cref="Db.Entities.Agent.LastUpdateCheckAt"/> — shown on the detail page's Identity card, at the user's explicit request.</summary>
    DateTimeOffset? LastUpdateCheckAt,
    /// <summary>See <see cref="Db.Entities.Agent.DesiredLogLevel"/> — shown/editable in the Settings dialog.</summary>
    string? DesiredLogLevel,
    /// <summary>See <see cref="Db.Entities.Agent.ActualLogLevel"/>.</summary>
    string? ActualLogLevel,
    /// <summary>See <see cref="Db.Entities.Agent.DesiredUpdateCheckIntervalMinutes"/>.</summary>
    int? DesiredUpdateCheckIntervalMinutes,
    /// <summary>See <see cref="Db.Entities.Agent.ActualUpdateCheckIntervalMinutes"/>.</summary>
    int? ActualUpdateCheckIntervalMinutes,
    /// <summary>See <see cref="Db.Entities.Agent.DesiredUpdateCheckJitterSeconds"/>.</summary>
    int? DesiredUpdateCheckJitterSeconds,
    /// <summary>See <see cref="Db.Entities.Agent.ActualUpdateCheckJitterSeconds"/>.</summary>
    int? ActualUpdateCheckJitterSeconds,
    /// <summary>See <see cref="Db.Entities.Agent.DesiredAliveIntervalMinutes"/> (server v1.3.20).</summary>
    int? DesiredAliveIntervalMinutes,
    /// <summary>See <see cref="Db.Entities.Agent.ActualAliveIntervalMinutes"/>.</summary>
    int? ActualAliveIntervalMinutes);

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

public record BulkDeleteRequest(IReadOnlyList<string> Hostnames);

public record BulkDeleteResult(int DeletedCount, IReadOnlyList<string> NotFoundHostnames);

public record BulkRebootRequest(IReadOnlyList<string> Hostnames);

public record BulkRebootResult(int TriggeredCount, IReadOnlyList<string> NotFoundHostnames);

/// <summary>Result of <c>POST /api/agents/settings</c> — see <see cref="BulkUpdateAgentSettingsRequest"/> for the bulk-push semantics.</summary>
public record BulkUpdateAgentSettingsResult(int UpdatedCount, IReadOnlyList<string> NotFoundHostnames);

/// <summary>
/// Result of an admin-initiated certificate re-issuance (updatewatch2-server#8).
/// <see cref="RegistrationToken"/> is the raw, one-shot registration token —
/// returned exactly once here, never persisted or retrievable again — for
/// the admin to place into the affected agent's local configuration.
/// </summary>
/// <summary>
/// <see cref="ErrorCode"/> is additive (updatewatch2-server#17) — null for
/// the "Agent not found." failure, since <see cref="Api.Controllers.AgentsController.ReissueCertificate"/>
/// maps that one straight to a bare 404 with no body at all, never
/// surfacing <see cref="FailureReason"/> to the web UI in the first place.
/// </summary>
public record ReissueCertificateResult(bool Success, string? RegistrationToken, string? FailureReason, ApiErrorCode? ErrorCode = null)
{
    public static ReissueCertificateResult Failed(string reason, ApiErrorCode? errorCode = null) => new(false, null, reason, errorCode);

    public static ReissueCertificateResult Succeeded(string registrationToken) => new(true, registrationToken, null);
}

/// <summary>
/// How a remote-triggered machine reboot went, as self-reported by the
/// agent once it has acted on the request — "Succeeded" only ever means
/// the platform's reboot command was scheduled successfully, not that the
/// machine has actually come back up yet (see
/// <see cref="Db.Entities.Agent.BootTimeUtc"/> for that). Mirrors
/// <c>Updates.InstallOutcome</c> field-for-field, including the same
/// <see cref="JsonStringEnumConverter"/> requirement — this project has no
/// global one configured, so without this attribute a wire body like
/// <c>{"outcome":"Succeeded"}</c> 400s against the default numeric
/// model-binding, per <c>Updates.InstallOutcome</c>'s own doc comment
/// confirmed live for that sibling enum. Kept as its own separate type
/// rather than reusing <c>Updates.InstallOutcome</c> even though the shape
/// is identical — a machine reboot is a distinct action from an OS-update
/// install, deliberately never conflated (CLAUDE.md's "update installation
/// never triggers a reboot itself... the admin decides when to actually
/// trigger a reboot" rule is exactly the distinction this type exists to
/// preserve on the wire).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RebootOutcome
{
    Succeeded,
    Failed,
}

/// <summary>
/// Body of <c>POST /api/agents/{hostname}/reboot-ack</c> — the agent's
/// acknowledgement that it acted on a pending reboot request.
/// <see cref="ErrorDetail"/> is only ever meaningful alongside
/// <see cref="RebootOutcome.Failed"/> (e.g. the platform-specific reboot
/// command itself failed to launch) — mirrors <c>Updates.InstallAckRequest</c>.
/// </summary>
public record RebootAckRequest(RebootOutcome Outcome, string? ErrorDetail = null);
