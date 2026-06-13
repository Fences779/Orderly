using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>SQLCipher-backed repository for the Commerce <see cref="Customer"/> (table <c>CommerceCustomers</c>).</summary>
public sealed class CommerceCustomerRepository : CommerceRepositoryBase<Customer>, ICommerceCustomerRepository
{
    private const string Table = "CommerceCustomers";

    public CommerceCustomerRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override string TableName => Table;

    protected override IReadOnlyList<string> EntityColumns { get; } = new[]
    {
        "WorkspaceId", "Name", "Phone", "WeChat", "Email", "LastOrderAt", "CompletedOrderCount", "TotalSpend",
    };

    protected override void BindEntity(SqliteCommand command, Customer entity)
    {
        command.Parameters.AddWithValue("$WorkspaceId", GuidToDb(entity.WorkspaceId));
        command.Parameters.AddWithValue("$Name", entity.Name);
        command.Parameters.AddWithValue("$Phone", TextToDb(entity.Phone));
        command.Parameters.AddWithValue("$WeChat", TextToDb(entity.WeChat));
        command.Parameters.AddWithValue("$Email", TextToDb(entity.Email));
        command.Parameters.AddWithValue("$LastOrderAt", DateTimeToDb(entity.LastOrderAt));
        command.Parameters.AddWithValue("$CompletedOrderCount", entity.CompletedOrderCount);
        command.Parameters.AddWithValue("$TotalSpend", MoneyToDb(entity.TotalSpend));
    }

    protected override Customer MapEntity(SqliteDataReader reader)
    {
        return new Customer
        {
            Id = GetGuid(reader, "Id"),
            CreatedAt = GetDateTime(reader, "CreatedAt"),
            WorkspaceId = GetGuid(reader, "WorkspaceId"),
            Name = GetString(reader, "Name"),
            Phone = GetStringNullable(reader, "Phone"),
            WeChat = GetStringNullable(reader, "WeChat"),
            Email = GetStringNullable(reader, "Email"),
            LastOrderAt = GetDateTimeNullable(reader, "LastOrderAt"),
            CompletedOrderCount = GetInt(reader, "CompletedOrderCount"),
            TotalSpend = GetMoney(reader, "TotalSpend"),
            CustomFieldsJson = GetStringNullable(reader, "CustomFieldsJson"),
        };
    }
}
