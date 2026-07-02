namespace Orderly.Contracts.Offline;

public sealed class CloudCacheEntryDto
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public long Revision { get; set; }
    public DateTime CachedAtUtc { get; set; }
}
