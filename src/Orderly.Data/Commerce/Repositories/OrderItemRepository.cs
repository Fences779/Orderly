using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="OrderItem"/> (table <c>CommerceOrderItems</c>).</summary>
public sealed class OrderItemRepository : CommerceRepositoryBase<OrderItem>, IOrderItemRepository
{
    private const string Table = "CommerceOrderItems";

    public OrderItemRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "OrderId", "ProductId", "ProductVariantId", "InventoryItemId", "UnitId",
        "Description", "Quantity", "UnitPrice", "UnitCost", "LineTotal",
    };

    protected override void BindEntity(SqliteCommand command, OrderItem entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$OrderId", GuidToDb(entity.OrderId));
        command.Parameters.AddWithValue("$ProductId", GuidToDb(entity.ProductId));
        command.Parameters.AddWithValue("$ProductVariantId", GuidToDb(entity.ProductVariantId));
        command.Parameters.AddWithValue("$InventoryItemId", GuidToDb(entity.InventoryItemId));
        command.Parameters.AddWithValue("$UnitId", GuidToDb(entity.UnitId));
        command.Parameters.AddWithValue("$Description", TextToDb(entity.Description));
        command.Parameters.AddWithValue("$Quantity", DecimalToDb(entity.Quantity));
        command.Parameters.AddWithValue("$UnitPrice", MoneyToDb(entity.UnitPrice));
        command.Parameters.AddWithValue("$UnitCost", MoneyToDb(entity.UnitCost));
        command.Parameters.AddWithValue("$LineTotal", MoneyToDb(entity.LineTotal));
    }

    protected override OrderItem MapEntity(SqliteDataReader reader)
    {
        return new OrderItem
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            OrderId = GetGuid(reader, "OrderId"),
            ProductId = GetGuidNullable(reader, "ProductId"),
            ProductVariantId = GetGuidNullable(reader, "ProductVariantId"),
            InventoryItemId = GetGuidNullable(reader, "InventoryItemId"),
            UnitId = GetGuidNullable(reader, "UnitId"),
            Description = GetStringNullable(reader, "Description"),
            Quantity = GetDecimal(reader, "Quantity"),
            UnitPrice = GetMoney(reader, "UnitPrice"),
            UnitCost = GetMoney(reader, "UnitCost"),
            LineTotal = GetMoney(reader, "LineTotal"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
