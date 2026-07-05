using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemotePriceChangeRequestService : IPriceChangeRequestService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;

    public RemotePriceChangeRequestService(RemoteCommerceClient client, CloudAuthSession session)
    {
        _client = client;
        _session = session;
    }

    public async Task SubmitAsync(Guid productId, decimal proposedPrice, string? reason, CancellationToken cancellationToken = default)
    {
        var command = new PriceChangeRequestCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = 0L,
            ProductId = productId,
            ProposedPrice = proposedPrice,
            ChangeReason = reason
        };

        await _client.PostAsync<PriceChangeRequestCommand>(
            $"api/workspaces/{_session.WorkspaceId:N}/price-change-requests",
            command,
            cancellationToken).ConfigureAwait(false);
    }
}
