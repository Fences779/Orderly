namespace Orderly.Core.Models;

public sealed class SyncRecord : EntityBase
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
    public DateTime? LastSyncedAt { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = string.Empty;
}
