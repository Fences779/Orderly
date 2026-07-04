using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;
using Orderly.Remote.Services;

namespace Orderly.Remote.Repositories;

public sealed class RemoteOrderRepository : RemoteCommerceRepositoryBase<Order, CloudOrderDto>, ICommerceOrderRepository
{
    public RemoteOrderRepository(RemoteCommerceClient client, CloudAuthSession session, ICloudCacheStore? cacheStore = null)
        : base(client, session, cacheStore) { }

    protected override string EntityPath => "orders";
    protected override string CacheEntityType => EntityType.Order;
    protected override Order Map(CloudOrderDto dto) => dto.ToEntity();

    public override async Task<Order> CreateAsync(Order entity, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        throw new InvalidOperationException("Remote orders must be created with line items.");
    }

    public async Task<Order> CreateAsync(Order entity, IEnumerable<OrderItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(items);

        var lineItems = items.ToList();
        if (lineItems.Count == 0)
        {
            throw new InvalidOperationException("Remote orders must contain at least one line item.");
        }

        var command = new CreateOrderCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = 0L,
            OrderNo = entity.OrderNo ?? string.Empty,
            CustomerId = entity.CustomerId,
            SalesStage = entity.SalesStage,
            PaymentStage = entity.PaymentStage,
            FulfillmentStage = entity.FulfillmentStage,
            OrderedAtUtc = entity.OrderedAt,
            Note = entity.Note,
            Items = lineItems.Select(MapItem).ToList()
        };

        var dto = await Client.PostAsync<CreateOrderCommand, CloudOrderDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/orders",
            command,
            cancellationToken).ConfigureAwait(false);

        return dto?.ToEntity() ?? entity;
    }

    private static CreateOrderItemCommand MapItem(OrderItem item) => new()
    {
        ProductId = item.ProductId,
        ProductVariantId = item.ProductVariantId,
        InventoryItemId = item.InventoryItemId,
        UnitId = item.UnitId,
        Description = item.Description ?? string.Empty,
        Quantity = item.Quantity,
        UnitPrice = item.UnitPrice.Amount,
        UnitCost = item.UnitCost.Amount
    };

    public override async Task UpdateAsync(Order entity, CancellationToken cancellationToken = default)
    {
        var latest = await Client.GetAsync<CloudOrderDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/orders/{entity.Id:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new UpdateOrderCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            OrderId = entity.Id,
            CustomerId = entity.CustomerId,
            SalesStage = entity.SalesStage,
            PaymentStage = entity.PaymentStage,
            FulfillmentStage = entity.FulfillmentStage,
            Note = entity.Note
        };

        await Client.PutAsync<UpdateOrderCommand, CloudOrderDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/orders/{entity.Id:N}",
            command,
            cancellationToken).ConfigureAwait(false);
    }

    public override async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var latest = await Client.GetAsync<CloudOrderDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/orders/{id:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new ArchiveCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            EntityType = EntityType.Order,
            EntityId = id,
            ArchiveReason = "Remote soft delete"
        };

        await Client.PostAsync<ArchiveCommand>(
            $"api/workspaces/{Session.WorkspaceId:N}/archive/{EntityType.Order}/{id:N}",
            command,
            cancellationToken).ConfigureAwait(false);
    }
}
