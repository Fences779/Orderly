namespace Orderly.Contracts.Sync;

public sealed class SnapshotRequest
{
    public string? EntityType { get; set; }
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
}
