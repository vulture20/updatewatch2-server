namespace UpdateWatch2.Server.Db.Entities;

/// <summary>One update an agent reported finding on its most recent update-check.</summary>
public class UpdateItem
{
    public int Id { get; set; }

    public int AgentId { get; set; }

    public Agent Agent { get; set; } = null!;

    public required string Title { get; set; }

    /// <summary>Vendor/package identifier (e.g. a KB number on Windows, a package name+version on Linux).</summary>
    public string? PackageId { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool Installed { get; set; }
}
