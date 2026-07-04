namespace Orderly.Contracts.Offline;

/// <summary>
/// Client-side cache for cloud entity snapshots. Backed by SQLCipher in production
/// and kept opaque to the rest of the application so the storage medium can change.
/// </summary>
public interface ICloudCacheStore
{
    Task<CloudCacheEntryDto?> GetAsync(string entityType, string entityId, CancellationToken cancellationToken = default);
    Task SetAsync(CloudCacheEntryDto entry, CancellationToken cancellationToken = default);
    Task ReplaceAllAsync(IEnumerable<CloudCacheEntryDto> entries, CancellationToken cancellationToken = default);
    Task RemoveAsync(string entityType, string entityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudCacheEntryDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
