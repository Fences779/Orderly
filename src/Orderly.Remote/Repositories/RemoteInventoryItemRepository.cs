using Orderly.Contracts.Commerce;
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
}
