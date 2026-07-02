using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;
using Orderly.Remote.Services;

namespace Orderly.Remote.Repositories;

public sealed class RemoteCashFlowEntryRepository : RemoteCommerceRepositoryBase<CashFlowEntry, CloudCashFlowEntryDto>, ICashFlowEntryRepository
{
    public RemoteCashFlowEntryRepository(RemoteCommerceClient client, CloudAuthSession session) : base(client, session) { }

    protected override string EntityPath => "cashflow/entries";
    protected override CashFlowEntry Map(CloudCashFlowEntryDto dto) => dto.ToEntity();
}
