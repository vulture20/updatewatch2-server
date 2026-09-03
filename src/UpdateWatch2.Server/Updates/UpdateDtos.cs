namespace UpdateWatch2.Server.Updates;

public record UpdateItemDto(int Id, string Title, string? PackageId, string? Description, DateTimeOffset DetectedAt, bool Installed);

/// <summary>One update-check report submitted by an agent (see CLAUDE.md section 2.2).</summary>
public record ReportUpdatesRequest(IReadOnlyList<ReportedUpdate> Updates, bool RebootRequired);

public record ReportedUpdate(string Title, string? PackageId, string? Description);
