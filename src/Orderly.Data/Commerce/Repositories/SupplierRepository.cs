using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="Supplier"/> (table <c>CommerceSuppliers</c>).</summary>
public sealed class SupplierRepository : CommerceRepositoryBase<Supplier>, ISupplierRepository
{
    private const string Table = "CommerceSuppliers";

    public SupplierRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "Name", "ContactName", "Phone", "Email", "Address", "Note",
    };

    protected override void BindEntity(SqliteCommand command, Supplier entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$Name", entity.Name);
        command.Parameters.AddWithValue("$ContactName", TextToDb(entity.ContactName));
        command.Parameters.AddWithValue("$Phone", TextToDb(entity.Phone));
        command.Parameters.AddWithValue("$Email", TextToDb(entity.Email));
        command.Parameters.AddWithValue("$Address", TextToDb(entity.Address));
        command.Parameters.AddWithValue("$Note", TextToDb(entity.Note));
    }

    protected override Supplier MapEntity(SqliteDataReader reader)
    {
        return new Supplier
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            Name = GetString(reader, "Name"),
            ContactName = GetStringNullable(reader, "ContactName"),
            Phone = GetStringNullable(reader, "Phone"),
            Email = GetStringNullable(reader, "Email"),
            Address = GetStringNullable(reader, "Address"),
            Note = GetStringNullable(reader, "Note"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
