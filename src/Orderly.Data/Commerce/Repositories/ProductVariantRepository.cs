using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="ProductVariant"/> (table <c>CommerceProductVariants</c>).</summary>
public sealed class ProductVariantRepository : CommerceRepositoryBase<ProductVariant>, IProductVariantRepository
{
    private const string Table = "CommerceProductVariants";

    public ProductVariantRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "ProductId", "Name", "Sku", "PriceAdjustment",
    };

    protected override void BindEntity(SqliteCommand command, ProductVariant entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$ProductId", GuidToDb(entity.ProductId));
        command.Parameters.AddWithValue("$Name", entity.Name);
        command.Parameters.AddWithValue("$Sku", TextToDb(entity.Sku));
        command.Parameters.AddWithValue("$PriceAdjustment", MoneyToDb(entity.PriceAdjustment));
    }

    protected override ProductVariant MapEntity(SqliteDataReader reader)
    {
        return new ProductVariant
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            ProductId = GetGuid(reader, "ProductId"),
            Name = GetString(reader, "Name"),
            Sku = GetStringNullable(reader, "Sku"),
            PriceAdjustment = GetMoney(reader, "PriceAdjustment"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
