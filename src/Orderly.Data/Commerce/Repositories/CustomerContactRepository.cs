using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for <see cref="CustomerContact"/> (table <c>CommerceCustomerContacts</c>).</summary>
public sealed class CustomerContactRepository : CommerceRepositoryBase<CustomerContact>, ICustomerContactRepository
{
    private const string Table = "CommerceCustomerContacts";

    public CustomerContactRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "CustomerId", "Name", "Phone", "Email", "Role", "IsPrimary",
    };

    protected override void BindEntity(SqliteCommand command, CustomerContact entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$CustomerId", GuidToDb(entity.CustomerId));
        command.Parameters.AddWithValue("$Name", entity.Name);
        command.Parameters.AddWithValue("$Phone", TextToDb(entity.Phone));
        command.Parameters.AddWithValue("$Email", TextToDb(entity.Email));
        command.Parameters.AddWithValue("$Role", TextToDb(entity.Role));
        command.Parameters.AddWithValue("$IsPrimary", entity.IsPrimary ? 1 : 0);
    }

    protected override CustomerContact MapEntity(SqliteDataReader reader)
    {
        return new CustomerContact
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            CustomerId = GetGuid(reader, "CustomerId"),
            Name = GetString(reader, "Name"),
            Phone = GetStringNullable(reader, "Phone"),
            Email = GetStringNullable(reader, "Email"),
            Role = GetStringNullable(reader, "Role"),
            IsPrimary = GetBool(reader, "IsPrimary"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
