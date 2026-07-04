using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for the Commerce <see cref="Order"/> (table <c>CommerceOrders</c>).</summary>
public sealed class CommerceOrderRepository : CommerceRepositoryBase<Order>, ICommerceOrderRepository
{
    private const string Table = "CommerceOrders";
    private readonly SqliteConnectionFactory _connectionFactory;

    public CommerceOrderRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "OrderNo", "CustomerId", "SalesStage", "PaymentStage", "FulfillmentStage",
        "Subtotal", "Total", "Cost", "GrossProfit", "GrossMargin", "PaidAmount", "ReceivableAmount", "OrderedAt", "Note",
    };

    protected override void BindEntity(SqliteCommand command, Order entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$OrderNo", TextToDb(entity.OrderNo));
        command.Parameters.AddWithValue("$CustomerId", GuidToDb(entity.CustomerId));
        command.Parameters.AddWithValue("$SalesStage", (int)entity.SalesStage);
        command.Parameters.AddWithValue("$PaymentStage", (int)entity.PaymentStage);
        command.Parameters.AddWithValue("$FulfillmentStage", (int)entity.FulfillmentStage);
        command.Parameters.AddWithValue("$Subtotal", MoneyToDb(entity.Subtotal));
        command.Parameters.AddWithValue("$Total", MoneyToDb(entity.Total));
        command.Parameters.AddWithValue("$Cost", MoneyToDb(entity.Cost));
        command.Parameters.AddWithValue("$GrossProfit", MoneyToDb(entity.GrossProfit));
        command.Parameters.AddWithValue("$GrossMargin", DecimalToDb(entity.GrossMargin));
        command.Parameters.AddWithValue("$PaidAmount", MoneyToDb(entity.PaidAmount));
        command.Parameters.AddWithValue("$ReceivableAmount", MoneyToDb(entity.ReceivableAmount));
        command.Parameters.AddWithValue("$OrderedAt", DateTimeToDb(entity.OrderedAt));
        command.Parameters.AddWithValue("$Note", TextToDb(entity.Note));
    }

    protected override Order MapEntity(SqliteDataReader reader)
    {
        return new Order
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            OrderNo = GetStringNullable(reader, "OrderNo"),
            CustomerId = GetGuidNullable(reader, "CustomerId"),
            SalesStage = GetEnum<OrderSalesStage>(reader, "SalesStage"),
            PaymentStage = GetEnum<OrderPaymentStage>(reader, "PaymentStage"),
            FulfillmentStage = GetEnum<OrderFulfillmentStage>(reader, "FulfillmentStage"),
            Subtotal = GetMoney(reader, "Subtotal"),
            Total = GetMoney(reader, "Total"),
            Cost = GetMoney(reader, "Cost"),
            GrossProfit = GetMoney(reader, "GrossProfit"),
            GrossMargin = GetDecimal(reader, "GrossMargin"),
            PaidAmount = GetMoney(reader, "PaidAmount"),
            ReceivableAmount = GetMoney(reader, "ReceivableAmount"),
            OrderedAt = GetDateTime(reader, "OrderedAt"),
            Note = GetStringNullable(reader, "Note"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }

    public async Task<Order> CreateAsync(Order entity, IEnumerable<OrderItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(items);

        var lineItems = items.ToList();
        if (lineItems.Count == 0)
        {
            throw new InvalidOperationException("订单必须包含至少一个订单项。");
        }

        using CoreWriteTransaction transaction = CoreWriteTransaction.Begin(_connectionFactory);
        await CreateAsync(entity, cancellationToken).ConfigureAwait(false);

        var itemRepository = new OrderItemRepository(_connectionFactory);
        foreach (OrderItem item in lineItems)
        {
            await itemRepository.CreateAsync(CloneForOrder(entity, item), cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }

    private static OrderItem CloneForOrder(Order order, OrderItem item) => new()
    {
        Id = item.Id,
        CreatedAt = item.CreatedAt,
        WorkspaceId = order.WorkspaceId,
        OrderId = order.Id,
        ProductId = item.ProductId,
        ProductVariantId = item.ProductVariantId,
        InventoryItemId = item.InventoryItemId,
        UnitId = item.UnitId,
        Description = item.Description,
        Quantity = item.Quantity,
        UnitPrice = item.UnitPrice,
        UnitCost = item.UnitCost,
        LineTotal = item.LineTotal,
        CustomFieldsJson = item.CustomFieldsJson
    };
}
