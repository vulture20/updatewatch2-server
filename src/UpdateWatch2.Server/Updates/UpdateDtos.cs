using System.Text.Json.Serialization;

namespace UpdateWatch2.Server.Updates;

public record UpdateItemDto(int Id, string Title, string? PackageId, string? Description, DateTimeOffset DetectedAt, bool Installed);

/// <summary>One update-check report submitted by an agent (see CLAUDE.md section 2.2).</summary>
public record ReportUpdatesRequest(IReadOnlyList<ReportedUpdate> Updates, bool RebootRequired);

public record ReportedUpdate(string Title, string? PackageId, string? Description);

/// <summary>
/// Body of <c>POST /api/agents/{hostname}/install</c> — an admin's way to
/// install only some of an agent's pending updates while sparing others,
/// at the user's explicit request ("Schaffe eine Möglichkeit nur bestimmte
/// Updates zu installieren und manche auszusparen"). <see cref="UpdateItemIds"/>
/// are <see cref="Db.Entities.UpdateItem.Id"/> values from the admin UI's
/// own selection checkboxes; a null/absent body (or a null field) installs
/// everything currently pending, the original behavior — kept working
/// unchanged for a caller with no specific selection to make, and how this
/// endpoint's own existing bare-POST tests keep exercising it.
/// </summary>
public record TriggerInstallRequest(IReadOnlyList<int>? UpdateItemIds);

/// <summary>
/// How a remote-triggered install (updatewatch2-server#10) went, as
/// self-reported by the agent once it has acted on the request — not to be
/// confused with <c>ReportUpdatesRequest.RebootRequired</c>, which is an
/// unrelated, always-present signal from the regular update-check cycle
/// (CLAUDE.md's "update installation never triggers a reboot itself" rule).
/// Serialized as its name ("Succeeded"/"Failed"), not the default
/// System.Text.Json numeric encoding — this project has no global
/// JsonStringEnumConverter (confirmed live: without this attribute, a
/// hand-typed curl body of <c>{"outcome":"Succeeded"}</c> 400s, since
/// ASP.NET Core's default model binding expects the bare number instead),
/// and an opaque 0/1 on this specific wire value would be inconsistent
/// with this codebase's own established preference for human-readable
/// wire/DB enum representations elsewhere (e.g. AdminSettings's
/// SmtpEncryption/AdEncryption columns, and this very outcome's own
/// LastInstallOutcome string field on the admin-facing side).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InstallOutcome
{
    Succeeded,
    Failed,
}

/// <summary>
/// Body of <c>POST /api/agents/{hostname}/install-ack</c> — the agent's
/// acknowledgement that it acted on a pending install request.
/// <see cref="ErrorDetail"/> (protocol 0.10.0) is only ever meaningful
/// alongside <see cref="InstallOutcome.Failed"/> — a human-readable reason
/// (the OS-level tool's own stderr/exit code, or a caught exception's
/// message) stored on <see cref="Db.Entities.Agent.LastInstallErrorDetail"/>
/// and surfaced on <c>AgentDetailPage</c>, added after a real production
/// failure ("apt-get ... exited with code 100: E: There were
/// unauthenticated packages...") took raising this agent's log level and
/// live-tailing journalctl to even see — before this, "Failed" was all
/// the admin UI could ever show. Additive/nullable, an older agent build
/// simply never sends it.
/// </summary>
public record InstallAckRequest(InstallOutcome Outcome, string? ErrorDetail = null);
