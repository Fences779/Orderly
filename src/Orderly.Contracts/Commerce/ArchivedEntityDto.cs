namespace Orderly.Contracts.Commerce;

/// <summary>
/// A lightweight summary of an archived Commerce entity returned by the workspace archive list API.
/// The exact set of populated fields depends on the entity type.
/// </summary>
public sealed class ArchivedEntityDto
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public string? ArchivedByDisplayName { get; set; }
    public string? ArchiveReason { get; set; }
    public long Revision { get; set; }
}
