using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;
using Orderly.Remote.Services;

namespace Orderly.Remote.Repositories;

public sealed class RemoteOrderRepository : RemoteCommerceRepositoryBase<Order, CloudOrderDto>, ICommerceOrderRepository
{
    public RemoteOrderRepository(RemoteCommerceClient client, CloudAuthSession session) : base(client, session) { }

    protected override string EntityPath => "orders";
    protected override Order Map(CloudOrderDto dto) => dto.ToEntity();
}
