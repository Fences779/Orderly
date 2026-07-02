using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;
using Orderly.Remote.Services;

namespace Orderly.Remote.Repositories;

public sealed class RemoteCustomerRepository : RemoteCommerceRepositoryBase<Customer, CloudCustomerDto>, ICommerceCustomerRepository
{
    public RemoteCustomerRepository(RemoteCommerceClient client, CloudAuthSession session) : base(client, session) { }

    protected override string EntityPath => "customers";
    protected override Customer Map(CloudCustomerDto dto) => dto.ToEntity();
}
