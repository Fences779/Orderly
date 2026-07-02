using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteProductService : IProductService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;

    public RemoteProductService(RemoteCommerceClient client, CloudAuthSession session)
    {
        _client = client;
        _session = session;
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var paged = await _client.GetAsync<PagedList<CloudProductDto>>($"api/workspaces/{_session.WorkspaceId:N}/products?pageSize=200", cancellationToken);
        if (paged == null) return Array.Empty<Product>();
        return paged.Items.Select(i => i.ToEntity()).ToList();
    }

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await _client.GetAsync<CloudProductDto>($"api/workspaces/{_session.WorkspaceId:N}/products/{id:N}", cancellationToken);
        return dto?.ToEntity();
    }

    public Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Remote product creation is not implemented in this stage.");

    public Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Remote product update is not implemented in this stage.");

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Remote product delete is not implemented in this stage.");
}
