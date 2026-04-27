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

    public async Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (customer.CreatedAt == default)
        {
            customer.CreatedAt = now;
        }

        customer.UpdatedAt = now;
        customer.DeletedAt = null;
        customer.IsSynced = false;
        customer.Version = Math.Max(1, customer.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Customers (
                Name, Status, Priority, SourcePlatform, Channel, ContactHandle, Phone, Remark, ExternalId, RawPayload,
                LastContactAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $name, $status, $priority, $sourcePlatform, $channel, $contactHandle, $phone, $remark, $externalId, $rawPayload,
                $lastContactAt, $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, customer);
        customer.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return customer;
    }

    public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        customer.UpdatedAt = DateTime.Now;
        customer.IsSynced = false;
        customer.Version = Math.Max(1, customer.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Customers
            SET Name = $name,
                Status = $status,
                Priority = $priority,
                SourcePlatform = $sourcePlatform,
                Channel = $channel,
                ContactHandle = $contactHandle,
                Phone = $phone,
                Remark = $remark,
                ExternalId = $externalId,
                RawPayload = $rawPayload,
                LastContactAt = $lastContactAt,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, customer);
        command.Parameters.AddWithValue("$id", customer.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now.ToString("O");
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Customers
            SET DeletedAt = $deletedAt,
                UpdatedAt = $updatedAt,
                IsSynced = 0,
                Version = Version + 1
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$deletedAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static void AddParameters(SqliteCommand command, Customer customer)
    {
        command.Parameters.AddWithValue("$name", customer.Name);
        command.Parameters.AddWithValue("$status", (int)customer.Status);
        command.Parameters.AddWithValue("$priority", (int)customer.Priority);
        command.Parameters.AddWithValue("$sourcePlatform", customer.SourcePlatform);
        command.Parameters.AddWithValue("$channel", customer.Channel);
        command.Parameters.AddWithValue("$contactHandle", customer.ContactHandle);
        command.Parameters.AddWithValue("$phone", customer.Phone);
        command.Parameters.AddWithValue("$remark", customer.Remark);
        command.Parameters.AddWithValue("$externalId", customer.ExternalId);
        command.Parameters.AddWithValue("$rawPayload", customer.RawPayload);
        command.Parameters.AddWithValue("$lastContactAt", ToDbDate(customer.LastContactAt));
        command.Parameters.AddWithValue("$createdAt", customer.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", customer.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(customer.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", customer.RemoteId);
        command.Parameters.AddWithValue("$isSynced", customer.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", customer.Version);
    }

    private static object ToDbDate(DateTime? value)
    {
        return value is null ? DBNull.Value : value.Value.ToString("O");
    }
}
