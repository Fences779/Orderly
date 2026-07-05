using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;
using Orderly.Remote.Services;

namespace Orderly.Remote.Repositories;

public sealed class RemoteInventoryItemRepository : RemoteCommerceRepositoryBase<InventoryItem, CloudInventoryItemDto>, IInventoryItemRepository
{
    public RemoteInventoryItemRepository(RemoteCommerceClient client, CloudAuthSession session, ICloudCacheStore? cacheStore = null)
        : base(client, session, cacheStore) { }

    protected override string EntityPath => "inventory/items";
    protected override string CacheEntityType => EntityType.InventoryItem;
    protected override InventoryItem Map(CloudInventoryItemDto dto) => dto.ToEntity();

    public override async Task<InventoryItem> CreateAsync(InventoryItem entity, CancellationToken cancellationToken = default)
    {
        var command = new CreateInventoryItemCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = 0L,
            Name = entity.Name,
            Sku = entity.Sku,
            ProductId = entity.ProductId,
            ProductVariantId = entity.ProductVariantId,
            UnitId = entity.UnitId,
            QuantityAvailable = entity.QuantityAvailable,
            ReorderThreshold = entity.ReorderThreshold,
            UnitCost = entity.UnitCost.Amount
        };

        var dto = await Client.PostAsync<CreateInventoryItemCommand, CloudInventoryItemDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/inventory/items",
            command,
            cancellationToken).ConfigureAwait(false);

        return dto?.ToEntity() ?? entity;
    }

    public override async Task UpdateAsync(InventoryItem entity, CancellationToken cancellationToken = default)
    {
        var latest = await Client.GetAsync<CloudInventoryItemDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/inventory/items/{entity.Id:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new UpdateInventoryItemCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            InventoryItemId = entity.Id,
            Name = entity.Name,
            Sku = entity.Sku,
            ProductId = entity.ProductId,
            ProductVariantId = entity.ProductVariantId,
            UnitId = entity.UnitId,
            QuantityAvailable = entity.QuantityAvailable,
            ReorderThreshold = entity.ReorderThreshold,
            UnitCost = entity.UnitCost.Amount
        };

        await Client.PutAsync<UpdateInventoryItemCommand>(
            $"api/workspaces/{Session.WorkspaceId:N}/inventory/items/{entity.Id:N}",
            command,
            cancellationToken).ConfigureAwait(false);
    }

    public override async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => await DeleteAsync(id, null, cancellationToken).ConfigureAwait(false);

    public override async Task DeleteAsync(Guid id, string? archiveReason, CancellationToken cancellationToken = default)
    {
        var latest = await Client.GetAsync<CloudInventoryItemDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/inventory/items/{id:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new ArchiveCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            EntityType = EntityType.InventoryItem,
            EntityId = id,
            ArchiveReason = archiveReason ?? "Remote soft delete"
        };

        await Client.PostAsync<ArchiveCommand>(
            $"api/workspaces/{Session.WorkspaceId:N}/archive/{EntityType.InventoryItem}/{id:N}",
            command,
            cancellationToken).ConfigureAwait(false);
    }
}
