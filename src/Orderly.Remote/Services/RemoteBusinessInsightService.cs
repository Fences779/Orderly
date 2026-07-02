using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteBusinessInsightService : IBusinessInsightService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;

    public RemoteBusinessInsightService(RemoteCommerceClient client, CloudAuthSession session)
    {
        _client = client;
        _session = session;
    }

    public async Task<IReadOnlyList<BusinessInsight>> GenerateInsightsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        var paged = await _client.GetAsync<PagedList<CloudBusinessInsightDto>>($"api/workspaces/{_session.WorkspaceId:N}/insights?pageSize=200", cancellationToken);
        if (paged == null) return Array.Empty<BusinessInsight>();
        return paged.Items.Select(i => i.ToEntity()).ToList();
    }

    public Task<IReadOnlyList<BusinessInsight>> PersistInsightsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
        => GenerateInsightsAsync(asOfUtc, cancellationToken);
}
