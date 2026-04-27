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

    public async Task<IReadOnlyList<MerchantOrder>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
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
            WHERE o.CustomerId = $customerId AND o.DeletedAt IS NULL AND c.DeletedAt IS NULL
            ORDER BY o.UpdatedAt DESC;
            """;
        command.Parameters.AddWithValue("$customerId", customerId);

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

    public async Task<MerchantOrder> CreateAsync(MerchantOrder order, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (order.CreatedAt == default)
        {
            order.CreatedAt = now;
        }

        order.UpdatedAt = now;
        order.DeletedAt = null;
        order.IsSynced = false;
        order.Version = Math.Max(1, order.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Orders (
                CustomerId, DealId, Title, Status, Amount, Requirement, SourcePlatform, Channel,
                ExternalId, RawPayload, NextFollowUpAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $dealId, $title, $status, $amount, $requirement, $sourcePlatform, $channel,
                $externalId, $rawPayload, $nextFollowUpAt, $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, order);
        order.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));

        return await GetByIdAsync(order.Id, cancellationToken) ?? order;
    }

    public async Task UpdateAsync(MerchantOrder order, CancellationToken cancellationToken = default)
    {
        order.UpdatedAt = DateTime.Now;
        order.IsSynced = false;
        order.Version = Math.Max(1, order.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Orders
            SET CustomerId = $customerId,
                DealId = $dealId,
                Title = $title,
                Status = $status,
                Amount = $amount,
                Requirement = $requirement,
                SourcePlatform = $sourcePlatform,
                Channel = $channel,
                ExternalId = $externalId,
                RawPayload = $rawPayload,
                NextFollowUpAt = $nextFollowUpAt,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, order);
        command.Parameters.AddWithValue("$id", order.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now.ToString("O");
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Orders
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

    private static void AddParameters(SqliteCommand command, MerchantOrder order)
    {
        command.Parameters.AddWithValue("$customerId", order.CustomerId);
        command.Parameters.AddWithValue("$dealId", ToDbInt(order.DealId));
        command.Parameters.AddWithValue("$title", order.Title);
        command.Parameters.AddWithValue("$status", (int)order.Status);
        command.Parameters.AddWithValue("$amount", order.Amount);
        command.Parameters.AddWithValue("$requirement", order.Requirement);
        command.Parameters.AddWithValue("$sourcePlatform", order.SourcePlatform);
        command.Parameters.AddWithValue("$channel", order.Channel);
        command.Parameters.AddWithValue("$externalId", order.ExternalId);
        command.Parameters.AddWithValue("$rawPayload", order.RawPayload);
        command.Parameters.AddWithValue("$nextFollowUpAt", ToDbDate(order.NextFollowUpAt));
        command.Parameters.AddWithValue("$createdAt", order.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", order.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(order.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", order.RemoteId);
        command.Parameters.AddWithValue("$isSynced", order.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", order.Version);
    }

    private static object ToDbInt(int? value)
    {
        return value is null ? DBNull.Value : value.Value;
    }

    private static object ToDbDate(DateTime? value)
    {
        return value is null ? DBNull.Value : value.Value.ToString("O");
    }
}
