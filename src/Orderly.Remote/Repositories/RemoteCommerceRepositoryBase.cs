using System.Text.Json;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Core.Commerce;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Repositories;

public abstract class RemoteCommerceRepositoryBase<TEntity, TDto>
    where TEntity : CommerceEntity
    where TDto : CloudEntityDto
{
    protected RemoteCommerceClient Client { get; }
    protected CloudAuthSession Session { get; }
    private readonly ICloudCacheStore? _cacheStore;
    protected abstract string EntityPath { get; }

    /// <summary>
    /// The logical entity type name used for offline cache keys. Must match the values broadcast
    /// by the server via SignalR (for example "order", "customer", "inventoryItem").
    /// </summary>
    protected abstract string CacheEntityType { get; }

    protected RemoteCommerceRepositoryBase(RemoteCommerceClient client, CloudAuthSession session, ICloudCacheStore? cacheStore = null)
    {
        Client = client;
        Session = session;
        _cacheStore = cacheStore;
    }

    protected abstract TEntity Map(TDto dto);

    public virtual Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
        => Task.FromResult(entity);

    public virtual async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = $"api/workspaces/{Session.WorkspaceId:N}/{EntityPath}/{id:N}";
        try
        {
            var dto = await Client.GetAsync<TDto>(path, cancellationToken);
            if (dto is not null)
            {
                await CacheEntryAsync(id.ToString("N"), dto, cancellationToken);
                return Map(dto);
            }
        }
        catch (HttpRequestException)
        {
            var cached = await TryGetCachedEntryAsync(id.ToString("N"), cancellationToken);
            if (cached is not null)
            {
                return Map(cached);
            }
            throw;
        }

        return null;
    }

    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var path = $"api/workspaces/{Session.WorkspaceId:N}/{EntityPath}?pageSize=200";
        try
        {
            var paged = await Client.GetAsync<PagedList<TDto>>(path, cancellationToken);
            if (paged is not null)
            {
                await CacheListAsync(paged, cancellationToken);
                return paged.Items.Select(Map).ToList();
            }
        }
        catch (HttpRequestException)
        {
            var cached = await TryGetCachedListAsync(cancellationToken);
            if (cached is not null)
            {
                return cached.Select(Map).ToList();
            }
            throw;
        }

        return Array.Empty<TEntity>();
    }

    public virtual async Task<TEntity?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
        => await GetByIdAsync(id, cancellationToken);

    public virtual async Task<IReadOnlyList<TEntity>> GetAllIncludingDeletedAsync(CancellationToken cancellationToken = default)
        => await GetAllAsync(cancellationToken);

    public virtual Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public virtual Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private async Task CacheEntryAsync(string entityId, TDto dto, CancellationToken cancellationToken)
    {
        if (_cacheStore is null)
        {
            return;
        }

        var entry = new CloudCacheEntryDto
        {
            EntityType = CacheEntityType,
            EntityId = entityId,
            PayloadJson = JsonSerializer.Serialize(dto),
            Revision = dto.Revision,
            CachedAtUtc = DateTime.UtcNow
        };
        await _cacheStore.SetAsync(entry, cancellationToken);
    }

    private async Task CacheListAsync(PagedList<TDto> paged, CancellationToken cancellationToken)
    {
        if (_cacheStore is null)
        {
            return;
        }

        var entry = new CloudCacheEntryDto
        {
            EntityType = CacheEntityType,
            EntityId = "all",
            PayloadJson = JsonSerializer.Serialize(paged.Items),
            Revision = paged.Items.Max(static x => x.Revision),
            CachedAtUtc = DateTime.UtcNow
        };
        await _cacheStore.SetAsync(entry, cancellationToken);
    }

    private async Task<TDto?> TryGetCachedEntryAsync(string entityId, CancellationToken cancellationToken)
    {
        if (_cacheStore is null)
        {
            return null;
        }

        var entry = await _cacheStore.GetAsync(CacheEntityType, entityId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<TDto>(entry.PayloadJson);
    }

    private async Task<IReadOnlyList<TDto>?> TryGetCachedListAsync(CancellationToken cancellationToken)
    {
        if (_cacheStore is null)
        {
            return null;
        }

        var entry = await _cacheStore.GetAsync(CacheEntityType, "all", cancellationToken);
        if (entry is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<List<TDto>>(entry.PayloadJson);
    }
}
