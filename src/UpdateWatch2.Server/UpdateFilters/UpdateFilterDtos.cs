using UpdateWatch2.Server.Api;

namespace UpdateWatch2.Server.UpdateFilters;

public record UpdateFilterDto(int Id, string Name, string Pattern, DateTimeOffset CreatedAt);

/// <summary>Body of both the create and the update endpoint — same two editable fields either way.</summary>
public record UpsertUpdateFilterRequest(string Name, string Pattern);

/// <summary>
/// Result of a create/update, mirroring <c>Agents.ReissueCertificateResult</c>'s
/// success/failure-reason shape. <see cref="FailureReason"/> is "Not found."
/// exactly (matched by string in <c>UpdateFiltersController</c>) when an
/// update targets an unknown id, distinguishing that from an ordinary
/// validation failure (empty name, invalid regex, duplicate name).
/// <see cref="ErrorCode"/>/<see cref="ErrorDetail"/> are additive
/// (updatewatch2-server#17) — both null for the "Not found." case, since
/// that one never reaches the web UI with a message body at all (a bare
/// 404 instead); see <see cref="ApiErrorCode.UpdateFilterPatternInvalid"/>'s
/// own doc comment for why that's the one validation failure here that
/// needs <see cref="ErrorDetail"/>.
/// </summary>
public record UpdateFilterResult(bool Success, UpdateFilterDto? Filter, string? FailureReason, ApiErrorCode? ErrorCode = null, string? ErrorDetail = null)
{
    public static UpdateFilterResult Failed(string reason, ApiErrorCode? errorCode = null, string? errorDetail = null) => new(false, null, reason, errorCode, errorDetail);

    public static UpdateFilterResult Succeeded(UpdateFilterDto filter) => new(true, filter, null);
}
