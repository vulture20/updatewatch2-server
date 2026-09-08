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
/// </summary>
public record UpdateFilterResult(bool Success, UpdateFilterDto? Filter, string? FailureReason)
{
    public static UpdateFilterResult Failed(string reason) => new(false, null, reason);

    public static UpdateFilterResult Succeeded(UpdateFilterDto filter) => new(true, filter, null);
}
