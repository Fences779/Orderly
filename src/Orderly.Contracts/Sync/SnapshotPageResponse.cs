namespace Orderly.Contracts.Sync;

public sealed class SnapshotPageResponse<T>
{
    public string SnapshotToken { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long TotalCount { get; set; }
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public long SnapshotSequence { get; set; }
}
