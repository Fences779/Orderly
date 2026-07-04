using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Contracts.Permissions;
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

    public async Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default)
    {
        var command = new CreateProductCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = 0L,
            Name = product.Name,
            Code = product.Code ?? string.Empty,
            ProductType = product.ProductType,
            Description = product.Description,
            DefaultUnitId = product.DefaultUnitId,
            SupplierId = product.SupplierId,
            DefaultPrice = product.DefaultPrice.Amount,
            DefaultCost = product.DefaultCost.Amount
        };

        var dto = await _client.PostAsync<CreateProductCommand, CloudProductDto>(
            $"api/workspaces/{_session.WorkspaceId:N}/products",
            command,
            cancellationToken).ConfigureAwait(false);

        return dto?.ToEntity() ?? product;
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        var latest = await _client.GetAsync<CloudProductDto>(
            $"api/workspaces/{_session.WorkspaceId:N}/products/{product.Id:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new UpdateProductCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            ProductId = product.Id,
            Name = product.Name,
            Code = product.Code,
            ProductType = product.ProductType,
            Description = product.Description,
            DefaultUnitId = product.DefaultUnitId,
            SupplierId = product.SupplierId,
            DefaultPrice = product.DefaultPrice.Amount,
            DefaultCost = product.DefaultCost.Amount
        };

        await _client.PutAsync<UpdateProductCommand>(
            $"api/workspaces/{_session.WorkspaceId:N}/products/{product.Id:N}",
            command,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var latest = await _client.GetAsync<CloudProductDto>(
            $"api/workspaces/{_session.WorkspaceId:N}/products/{id:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new ArchiveCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            EntityType = EntityType.Product,
            EntityId = id,
            ArchiveReason = "Remote soft delete"
        };

        await _client.PostAsync<ArchiveCommand>(
            $"api/workspaces/{_session.WorkspaceId:N}/archive/{EntityType.Product}/{id:N}",
            command,
            cancellationToken).ConfigureAwait(false);
    }
}
