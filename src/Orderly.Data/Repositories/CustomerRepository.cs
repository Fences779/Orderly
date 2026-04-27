using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public CustomerRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Status, Priority, SourcePlatform, Channel, ContactHandle, Phone, Remark, ExternalId, RawPayload,
                   LastContactAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM Customers
            WHERE DeletedAt IS NULL
            ORDER BY UpdatedAt DESC;
            """;

        var rows = new List<Customer>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    public async Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Status, Priority, SourcePlatform, Channel, ContactHandle, Phone, Remark, ExternalId, RawPayload,
                   LastContactAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM Customers
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    internal static Customer Map(SqliteDataReader reader)
    {
        return Map(reader, 0);
    }

    internal static Customer Map(SqliteDataReader reader, int offset)
    {
        return new Customer
        {
            Id = reader.GetInt32(offset),
            Name = reader.GetString(offset + 1),
            Status = (CustomerStatus)reader.GetInt32(offset + 2),
            Priority = (CustomerPriority)reader.GetInt32(offset + 3),
            SourcePlatform = reader.GetString(offset + 4),
            Channel = reader.GetString(offset + 5),
            ContactHandle = reader.GetString(offset + 6),
            Phone = reader.GetString(offset + 7),
            Remark = reader.GetString(offset + 8),
            ExternalId = reader.GetString(offset + 9),
            RawPayload = reader.GetString(offset + 10),
            LastContactAt = reader.IsDBNull(offset + 11) ? null : DateTime.Parse(reader.GetString(offset + 11), null, DateTimeStyles.RoundtripKind),
            CreatedAt = DateTime.Parse(reader.GetString(offset + 12), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(offset + 13), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(offset + 14) ? null : DateTime.Parse(reader.GetString(offset + 14), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(offset + 15),
            IsSynced = reader.GetInt32(offset + 16) == 1,
            Version = reader.GetInt32(offset + 17)
        };
    }
}
