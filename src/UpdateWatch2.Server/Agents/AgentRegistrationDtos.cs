using UpdateWatch2.Server.AgentUpdates;

namespace UpdateWatch2.Server.Agents;

/// <summary>
/// Body of a <c>POST /api/agents/{hostname}/register</c> call — hostname
/// itself comes from the route, not this body (the route is the single
/// source of truth for identity, per CLAUDE.md "Agents are identified by
/// hostname"). <see cref="RegistrationToken"/> is omitted on an agent's very
/// first contact and present on every poll after that — see
/// <see cref="AgentRegistrationService"/>'s state-machine doc comment.
/// </summary>
public record AgentRegisterRequest(
    string? DnsName,
    string? OperatingSystem,
    string? IpAddress,
    string? AgentVersion,
    string? ProtocolVersion,
    string? RegistrationToken);

public enum AgentRegistrationStatus
{
    /// <summary>No/mismatched token for an already-known hostname, or a token that doesn't verify — the anti-hijack case.</summary>
    Rejected,

    /// <summary>Registered (or already known) but not yet approved by an admin.</summary>
    Pending,

    /// <summary>Approved. <see cref="AgentRegistrationOutcome.CertificatePfxBase64"/> carries the cert exactly once.</summary>
    Approved,
}

public record AgentRegistrationOutcome(AgentRegistrationStatus Status, string? RegistrationToken, string? CertificatePfxBase64, string? FailureReason)
{
    public static AgentRegistrationOutcome Rejected(string reason) => new(AgentRegistrationStatus.Rejected, null, null, reason);

    public static AgentRegistrationOutcome Pending(string? rawToken) => new(AgentRegistrationStatus.Pending, rawToken, null, null);

    public static AgentRegistrationOutcome Approved(string? certificatePfxBase64) => new(AgentRegistrationStatus.Approved, null, certificatePfxBase64, null);
}

/// <summary>
/// Optional body of <c>POST /api/agents/{hostname}/alive</c>
/// (updatewatch2-agent#6) — an agent's self-reported metadata can change
/// after onboarding (DHCP lease renewal, an OS upgrade, a hostname/domain
/// change, an agent binary upgrade), but <see cref="AgentRegistrationService.RegisterAsync"/>
/// never runs again for an already-certified agent, so the alive heartbeat
/// is the only remaining channel to keep it current. Nullable/all-optional
/// rather than required: an agent built before this field existed sends no
/// body at all, and that must keep working exactly as before (just with no
/// metadata refresh), not fail the heartbeat. <see cref="BootTimeUtc"/> is
/// the same idea applied to <see cref="Db.Entities.Agent.BootTimeUtc"/> —
/// added later than the other four fields, so it's independently nullable
/// even on an agent build new enough to send everything else.
/// <see cref="RebootRequired"/> is a different thing again — not identity
/// metadata, but a fresh, lightweight check of the same "a restart is
/// needed to finish already-installed updates" signal the agent's own
/// periodic full update-check report already carries, now also riding
/// every heartbeat so it's checked far more often — at the user's explicit
/// request ("Der Check, ob ein Neustart nötig ist, sollte öfter
/// stattfinden."). Null means the agent's own check failed or hasn't run
/// this tick, never a confirmed false — see <see cref="AgentRegistrationService.RecordAliveAsync"/>'s
/// handling for why that distinction is preserved through to the stored value.
/// <see cref="ActualLogLevel"/>/<see cref="ActualUpdateCheckIntervalMinutes"/>/
/// <see cref="ActualUpdateCheckJitterSeconds"/>/<see cref="ActualAliveIntervalMinutes"/>
/// are the agent's own current, actually-effective values for the settings
/// the server can push an override for (<see cref="Db.Entities.Agent.DesiredLogLevel"/>
/// and its siblings) — sent every heartbeat regardless of whether an
/// override is set, purely for admin visibility, at the user's explicit
/// request ("Änderungen an Registry bzw. Configfile sollen wiederum am
/// Server zu sehen sein."). <see cref="ActualAliveIntervalMinutes"/> was
/// added later than the other three (server v1.3.20) — same reasoning as
/// <see cref="BootTimeUtc"/> above, independently nullable/optional even on
/// an agent build new enough to send everything else.
/// </summary>
public record AgentAliveRequest(
    string? DnsName, string? OperatingSystem, string? IpAddress, string? AgentVersion, DateTimeOffset? BootTimeUtc = null,
    bool? RebootRequired = null, string? ActualLogLevel = null, int? ActualUpdateCheckIntervalMinutes = null,
    int? ActualUpdateCheckJitterSeconds = null, int? ActualAliveIntervalMinutes = null);

/// <summary>
/// Result of a recorded alive heartbeat (updatewatch2-server#10) —
/// <see cref="InstallRequested"/> mirrors <c>Agent.PendingInstallRequestedAt</c>
/// being set, so <see cref="Api.Controllers.AgentProtocolController.Alive"/>
/// can hand it back to the agent in the same round-trip rather than needing
/// a second poll endpoint. <see cref="InstallUpdateIds"/> mirrors
/// <c>Agent.PendingInstallUpdateIds</c> — the specific PackageIds an admin
/// selected, if any (null means "everything currently pending", installing
/// only some updates while sparing others is otherwise the whole point of
/// this field existing at all). <see cref="UpdateAvailable"/> is the same idea
/// applied to a newer agent *software* release (updatewatch2-server#14) —
/// null whenever there's nothing to offer (feature disabled, no known
/// release, or this agent is already current).
/// <see cref="CertificateRotationPending"/> is the same shape again, applied
/// to CA root rotation (updatewatch2-server#6): true whenever this agent's
/// stored <c>Agent.IssuingRootThumbprint</c> is known and no longer matches
/// the CA's CURRENT root, prompting the agent to renew immediately instead
/// of waiting for its own expiry-driven schedule — see
/// <c>AgentRegistrationService.RecordAliveAsync</c> for how it's computed.
/// Self-correcting with no acknowledgement needed: once the agent renews,
/// its next heartbeat computes this as false on its own.
/// <see cref="RebootRequested"/> is the same delivery mechanism again,
/// mirroring <see cref="InstallRequested"/> exactly — true whenever
/// <c>Agent.PendingRebootRequestedAt</c> is set, cleared once the agent
/// acknowledges via <c>POST .../reboot-ack</c>.
/// <see cref="DesiredLogLevel"/>/<see cref="DesiredUpdateCheckIntervalMinutes"/>/
/// <see cref="DesiredUpdateCheckJitterSeconds"/> mirror the identically-named
/// <c>Agent</c> columns directly — null means no admin override for that
/// setting, in which case the agent's own local registry/config file value
/// stays authoritative. Non-null is enforced unconditionally by the agent on
/// every heartbeat that reports a differing actual value, which is what
/// implements "the server always wins on conflict" (CLAUDE.md, at the
/// user's explicit request) — no separate timestamp-based conflict
/// resolution needed.
/// </summary>
public record AliveRecordResult(
    bool InstallRequested, IReadOnlyList<string>? InstallUpdateIds, AgentUpdateOffer? UpdateAvailable, bool CertificateRotationPending,
    bool RebootRequested, bool PreDownloadWindowsUpdatesEnabled, string? DesiredLogLevel, int? DesiredUpdateCheckIntervalMinutes,
    int? DesiredUpdateCheckJitterSeconds, int? DesiredAliveIntervalMinutes);

/// <summary>
/// The actual JSON shape of <c>POST /api/agents/{hostname}/alive</c>'s
/// response body — kept as an explicit, named, independently testable type
/// rather than the inline anonymous object <see cref="Api.Controllers.AgentProtocolController.Alive"/>
/// used to build by hand. Found by a user report ("Änderungen werden
/// aktuell nicht in die Registry geschrieben. Egal, was ausgewählt oder
/// eingetragen wird."): that anonymous object listed each field out
/// individually, and when <see cref="DesiredLogLevel"/>/its siblings were
/// added to <see cref="AliveRecordResult"/> (server v1.3.14), nobody also
/// added them to that list — <c>Ok(result)</c> would have serialized the
/// whole record automatically and caught this, but the anonymous object's
/// own field-by-field shape silently drops anything not explicitly named
/// in it, with no compiler warning and no failing test (this codebase has
/// no automated coverage of this controller's actual response body at
/// all — <c>WebApplicationFactory</c>'s in-memory <c>TestServer</c> can't
/// present a client certificate to reach the success path in the first
/// place, see <c>UpdatesEndpointTests</c>' own doc comment on the same
/// limitation). <see cref="AgentUpdateAvailable"/> is deliberately renamed
/// from <see cref="AliveRecordResult.UpdateAvailable"/> — must keep
/// matching the agent-side <c>AliveResult.AgentUpdateAvailable</c> field
/// name exactly, which is why this can't just be <c>Ok(result)</c> even
/// now.
/// </summary>
public record AliveResponseDto(
    bool InstallRequested,
    IReadOnlyList<string>? InstallUpdateIds,
    AgentUpdateOffer? AgentUpdateAvailable,
    bool CertificateRotationPending,
    bool RebootRequested,
    bool PreDownloadWindowsUpdatesEnabled,
    string? DesiredLogLevel,
    int? DesiredUpdateCheckIntervalMinutes,
    int? DesiredUpdateCheckJitterSeconds,
    int? DesiredAliveIntervalMinutes)
{
    public static AliveResponseDto FromResult(AliveRecordResult result) => new(
        result.InstallRequested, result.InstallUpdateIds, result.UpdateAvailable, result.CertificateRotationPending,
        result.RebootRequested, result.PreDownloadWindowsUpdatesEnabled, result.DesiredLogLevel,
        result.DesiredUpdateCheckIntervalMinutes, result.DesiredUpdateCheckJitterSeconds, result.DesiredAliveIntervalMinutes);
}

/// <summary>
/// Body of <c>PUT /api/agents/{hostname}/settings</c> — an admin's way to
/// set the current value for one or more of the settings the server keeps
/// bidirectionally synced with a specific agent, at the user's explicit
/// request ("LogLevel des Agents über den Server setzen... Änderungen
/// sollen auf beiden Seiten möglich sein und direkt auf die Gegenseite
/// gespiegelt werden. Diese Logik soll für alle (auch spätere)
/// Einstellungen am Server für den Agent gelten."). All four fields are
/// required — there is no longer a "clear to defer to the local value"
/// concept: the admin UI always shows and submits the agent's actual
/// current value, edited in place, matching <see cref="Db.Entities.Agent.DesiredLogLevel"/>'s
/// own doc comment. A full replace, not a partial merge — matching
/// <c>PUT /api/admin/settings</c>'s own convention, so the per-agent
/// Settings dialog always submits all four together.
/// <see cref="DesiredAliveIntervalMinutes"/> was added later than the other
/// three (server v1.3.20, at the user's explicit request). See
/// <see cref="AgentSettingsValidator"/> for the accepted value ranges. Not
/// to be confused with <see cref="BulkUpdateAgentSettingsRequest"/>, the
/// overview list's bulk-push counterpart, whose fields are each
/// independently optional instead.
/// </summary>
public record UpdateAgentSettingsRequest(
    string DesiredLogLevel, int DesiredUpdateCheckIntervalMinutes, int DesiredUpdateCheckJitterSeconds,
    int DesiredAliveIntervalMinutes);

/// <summary>
/// Body of <c>POST /api/agents/settings</c> — the overview list's bulk
/// counterpart to <see cref="UpdateAgentSettingsRequest"/>, at the user's
/// explicit request ("Es fehlt außerdem die Möglichkeit Agent-Einstellungen
/// bulk zu pushen."). Unlike the single-agent request, every settings field
/// here is independently optional/nullable: null means "leave this setting
/// untouched on every selected agent," letting an admin push just one
/// setting (e.g. LogLevel=DEBUG on several agents at once for diagnosis)
/// without being forced to also overwrite the others' individually-tuned
/// values — confirmed as the intended design via an explicit clarifying
/// question before implementing, rather than assumed. At least one field
/// must be non-null (see <see cref="AgentSettingsValidator.IsValidBulkRequest"/>)
/// — a request with every field null would be a no-op push that still sets
/// <see cref="Db.Entities.Agent.PendingSettingsPush"/> on every selected
/// agent for nothing.
/// </summary>
public record BulkUpdateAgentSettingsRequest(
    IReadOnlyList<string> Hostnames, string? DesiredLogLevel, int? DesiredUpdateCheckIntervalMinutes,
    int? DesiredUpdateCheckJitterSeconds, int? DesiredAliveIntervalMinutes);

/// <summary>
/// Result of <c>POST /api/agents/{hostname}/renew</c> (updatewatch2-server#7)
/// — reached only over the agent-facing mTLS listener, authenticated by the
/// agent's CURRENT still-valid client certificate rather than a token. Not
/// to be confused with <see cref="AgentRegistrationOutcome"/>: renewal never
/// touches <see cref="AgentRegistrationStatus"/> or the registration-token
/// flow, it only re-issues a leaf for an agent already fully onboarded.
/// </summary>
public record RenewCertificateResult(bool Success, string? CertificatePfxBase64, string? FailureReason)
{
    public static RenewCertificateResult Failed(string reason) => new(false, null, reason);

    public static RenewCertificateResult Succeeded(string certificatePfxBase64) => new(true, certificatePfxBase64, null);
}
