namespace Orderly.Contracts.Sync;

public sealed class SyncStateDto
{
    public Guid WorkspaceId { get; set; }
    public long LastSequence { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
