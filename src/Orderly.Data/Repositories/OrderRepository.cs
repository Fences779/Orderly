using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class OrderRepository : IOrderRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public OrderRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<MerchantOrder>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                o.Id, o.CustomerId, o.DealId, o.Title, o.Status, o.Amount, o.Requirement, o.SourcePlatform, o.Channel,
                o.ExternalId, o.RawPayload, o.NextFollowUpAt, o.CreatedAt, o.UpdatedAt, o.DeletedAt, o.RemoteId, o.IsSynced, o.Version,
                c.Id, c.Name, c.Status, c.Priority, c.SourcePlatform, c.Channel, c.ContactHandle, c.Phone, c.Remark, c.ExternalId, c.RawPayload,
                c.LastContactAt, c.CreatedAt, c.UpdatedAt, c.DeletedAt, c.RemoteId, c.IsSynced, c.Version
            FROM Orders o
            INNER JOIN Customers c ON c.Id = o.CustomerId
            WHERE o.DeletedAt IS NULL AND c.DeletedAt IS NULL
            ORDER BY o.UpdatedAt DESC;
            """;

        var rows = new List<MerchantOrder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(MapWithCustomer(reader));
        }

        return rows;
    }

    public async Task<MerchantOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                o.Id, o.CustomerId, o.DealId, o.Title, o.Status, o.Amount, o.Requirement, o.SourcePlatform, o.Channel,
                o.ExternalId, o.RawPayload, o.NextFollowUpAt, o.CreatedAt, o.UpdatedAt, o.DeletedAt, o.RemoteId, o.IsSynced, o.Version,
                c.Id, c.Name, c.Status, c.Priority, c.SourcePlatform, c.Channel, c.ContactHandle, c.Phone, c.Remark, c.ExternalId, c.RawPayload,
                c.LastContactAt, c.CreatedAt, c.UpdatedAt, c.DeletedAt, c.RemoteId, c.IsSynced, c.Version
            FROM Orders o
            INNER JOIN Customers c ON c.Id = o.CustomerId
            WHERE o.Id = $id AND o.DeletedAt IS NULL AND c.DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapWithCustomer(reader) : null;
    }

    private static MerchantOrder MapWithCustomer(SqliteDataReader reader)
    {
        return new MerchantOrder
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            DealId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            Title = reader.GetString(3),
            Status = (OrderStatus)reader.GetInt32(4),
            Amount = reader.GetDecimal(5),
            Requirement = reader.GetString(6),
            SourcePlatform = reader.GetString(7),
            Channel = reader.GetString(8),
            ExternalId = reader.GetString(9),
            RawPayload = reader.GetString(10),
            NextFollowUpAt = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
            CreatedAt = DateTime.Parse(reader.GetString(12), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(13), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(14) ? null : DateTime.Parse(reader.GetString(14), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(15),
            IsSynced = reader.GetInt32(16) == 1,
            Version = reader.GetInt32(17),
            Customer = CustomerRepository.Map(reader, 18)
        };
    }
}
