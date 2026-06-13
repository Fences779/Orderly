using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="InventoryMovement"/> (table <c>CommerceInventoryMovements</c>).</summary>
public sealed class InventoryMovementRepository : CommerceRepositoryBase<InventoryMovement>, IInventoryMovementRepository
{
    private const string Table = "CommerceInventoryMovements";

    public InventoryMovementRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "InventoryItemId", "MovementType", "Quantity", "SupplierId", "OrderId", "OccurredAt", "BusinessKey", "Note",
    };

    protected override void BindEntity(SqliteCommand command, InventoryMovement entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$InventoryItemId", GuidToDb(entity.InventoryItemId));
        command.Parameters.AddWithValue("$MovementType", (int)entity.MovementType);
        command.Parameters.AddWithValue("$Quantity", DecimalToDb(entity.Quantity));
        command.Parameters.AddWithValue("$SupplierId", GuidToDb(entity.SupplierId));
        command.Parameters.AddWithValue("$OrderId", GuidToDb(entity.OrderId));
        command.Parameters.AddWithValue("$OccurredAt", DateTimeToDb(entity.OccurredAt));
        command.Parameters.AddWithValue("$BusinessKey", TextToDb(entity.BusinessKey));
        command.Parameters.AddWithValue("$Note", TextToDb(entity.Note));
    }

    protected override InventoryMovement MapEntity(SqliteDataReader reader)
    {
        return new InventoryMovement
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            InventoryItemId = GetGuid(reader, "InventoryItemId"),
            MovementType = GetEnum<InventoryMovementType>(reader, "MovementType"),
            Quantity = GetDecimal(reader, "Quantity"),
            SupplierId = GetGuidNullable(reader, "SupplierId"),
            OrderId = GetGuidNullable(reader, "OrderId"),
            OccurredAt = GetDateTime(reader, "OccurredAt"),
            BusinessKey = GetStringNullable(reader, "BusinessKey"),
            Note = GetStringNullable(reader, "Note"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
