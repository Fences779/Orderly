using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="InventoryItem"/> (table <c>CommerceInventoryItems</c>).</summary>
public sealed class InventoryItemRepository : CommerceRepositoryBase<InventoryItem>, IInventoryItemRepository
{
    private const string Table = "CommerceInventoryItems";

    public InventoryItemRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "Name", "Sku", "ProductId", "ProductVariantId", "UnitId", "QuantityAvailable", "ReorderThreshold", "UnitCost",
    };

    protected override void BindEntity(SqliteCommand command, InventoryItem entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$Name", entity.Name);
        command.Parameters.AddWithValue("$Sku", TextToDb(entity.Sku));
        command.Parameters.AddWithValue("$ProductId", GuidToDb(entity.ProductId));
        command.Parameters.AddWithValue("$ProductVariantId", GuidToDb(entity.ProductVariantId));
        command.Parameters.AddWithValue("$UnitId", GuidToDb(entity.UnitId));
        command.Parameters.AddWithValue("$QuantityAvailable", DecimalToDb(entity.QuantityAvailable));
        command.Parameters.AddWithValue("$ReorderThreshold", DecimalToDb(entity.ReorderThreshold));
        command.Parameters.AddWithValue("$UnitCost", MoneyToDb(entity.UnitCost));
    }

    protected override InventoryItem MapEntity(SqliteDataReader reader)
    {
        return new InventoryItem
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            Name = GetString(reader, "Name"),
            Sku = GetStringNullable(reader, "Sku"),
            ProductId = GetGuidNullable(reader, "ProductId"),
            ProductVariantId = GetGuidNullable(reader, "ProductVariantId"),
            UnitId = GetGuidNullable(reader, "UnitId"),
            QuantityAvailable = GetDecimal(reader, "QuantityAvailable"),
            ReorderThreshold = GetDecimal(reader, "ReorderThreshold"),
            UnitCost = GetMoney(reader, "UnitCost"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
