namespace Orderly.Contracts.Sync;

public sealed class SnapshotTokenResponse
{
    public string SnapshotToken { get; set; } = string.Empty;
    public long SnapshotSequence { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
