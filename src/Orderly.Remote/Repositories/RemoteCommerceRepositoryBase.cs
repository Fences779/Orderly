using Orderly.Contracts.Commerce;
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
    protected abstract string EntityPath { get; }

    protected RemoteCommerceRepositoryBase(RemoteCommerceClient client, CloudAuthSession session)
    {
        Client = client;
        Session = session;
    }

    protected abstract TEntity Map(TDto dto);

    public virtual Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
        => Task.FromResult(entity);

    public virtual async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await Client.GetAsync<TDto>($"api/workspaces/{Session.WorkspaceId:N}/{EntityPath}/{id:N}", cancellationToken);
        return dto == null ? null : Map(dto);
    }

    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var paged = await Client.GetAsync<PagedList<TDto>>($"api/workspaces/{Session.WorkspaceId:N}/{EntityPath}?pageSize=200", cancellationToken);
        if (paged == null) return Array.Empty<TEntity>();
        return paged.Items.Select(Map).ToList();
    }

    public virtual async Task<TEntity?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
        => await GetByIdAsync(id, cancellationToken);

    public virtual async Task<IReadOnlyList<TEntity>> GetAllIncludingDeletedAsync(CancellationToken cancellationToken = default)
        => await GetAllAsync(cancellationToken);

    public virtual Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public virtual Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
