namespace Orderly.Contracts.Commerce;

public sealed class ArchiveCommand : WriteCommandBase
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? ArchiveReason { get; set; }
}
