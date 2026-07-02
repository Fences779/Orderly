using System.Collections.Concurrent;
using Orderly.Contracts.Offline;

namespace Orderly.Remote.Cache;

public sealed class CloudCacheStore
{
    private readonly ConcurrentDictionary<string, CloudCacheEntryDto> _entries = new();

    public Task<CloudCacheEntryDto?> GetAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        _entries.TryGetValue(GetKey(entityType, entityId), out var entry);
        return Task.FromResult(entry);
    }

    public Task SetAsync(CloudCacheEntryDto entry, CancellationToken cancellationToken = default)
    {
        _entries[GetKey(entry.EntityType, entry.EntityId)] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(GetKey(entityType, entityId), out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CloudCacheEntryDto>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CloudCacheEntryDto>>(_entries.Values.ToList());

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _entries.Clear();
        return Task.CompletedTask;
    }

    private static string GetKey(string entityType, string entityId) => $"{entityType}:{entityId}";
}
