using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="Product"/> (table <c>CommerceProducts</c>).</summary>
public sealed class ProductRepository : CommerceRepositoryBase<Product>, IProductRepository
{
    private const string Table = "CommerceProducts";

    public ProductRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "Name", "Code", "ProductType", "Description", "DefaultUnitId", "SupplierId", "DefaultPrice", "DefaultCost",
    };

    protected override void BindEntity(SqliteCommand command, Product entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$Name", entity.Name);
        command.Parameters.AddWithValue("$Code", TextToDb(entity.Code));
        command.Parameters.AddWithValue("$ProductType", (int)entity.ProductType);
        command.Parameters.AddWithValue("$Description", TextToDb(entity.Description));
        command.Parameters.AddWithValue("$DefaultUnitId", GuidToDb(entity.DefaultUnitId));
        command.Parameters.AddWithValue("$SupplierId", GuidToDb(entity.SupplierId));
        command.Parameters.AddWithValue("$DefaultPrice", MoneyToDb(entity.DefaultPrice));
        command.Parameters.AddWithValue("$DefaultCost", MoneyToDb(entity.DefaultCost));
    }

    protected override Product MapEntity(SqliteDataReader reader)
    {
        return new Product
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            Name = GetString(reader, "Name"),
            Code = GetStringNullable(reader, "Code"),
            ProductType = GetEnum<ProductType>(reader, "ProductType"),
            Description = GetStringNullable(reader, "Description"),
            DefaultUnitId = GetGuidNullable(reader, "DefaultUnitId"),
            SupplierId = GetGuidNullable(reader, "SupplierId"),
            DefaultPrice = GetMoney(reader, "DefaultPrice"),
            DefaultCost = GetMoney(reader, "DefaultCost"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
