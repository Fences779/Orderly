using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;
using Orderly.Remote.Services;

namespace Orderly.Remote.Repositories;

public sealed class RemoteInventoryItemRepository : RemoteCommerceRepositoryBase<InventoryItem, CloudInventoryItemDto>, IInventoryItemRepository
{
    public RemoteInventoryItemRepository(RemoteCommerceClient client, CloudAuthSession session) : base(client, session) { }

    protected override string EntityPath => "inventory/items";
    protected override InventoryItem Map(CloudInventoryItemDto dto) => dto.ToEntity();

    public override Task<InventoryItem> CreateAsync(InventoryItem entity, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Inventory items are created indirectly through products or stocktake adjustments in the cloud runtime.");

    public override Task UpdateAsync(InventoryItem entity, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Inventory item master data updates are not exposed remotely; use inventory movements to adjust quantity.");

    public override async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
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
            ArchiveReason = "Remote soft delete"
        };

        await Client.PostAsync<ArchiveCommand>(
            $"api/workspaces/{Session.WorkspaceId:N}/archive/{EntityType.InventoryItem}/{id:N}",
            command,
            cancellationToken).ConfigureAwait(false);
    }
}
