using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class ConversationMessageRepository : IConversationMessageRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ConversationMessageRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ConversationMessage> CreateAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (message.CreatedAt == default)
        {
            message.CreatedAt = now;
        }

        message.UpdatedAt = now;
        message.DeletedAt = null;
        message.IsSynced = false;
        message.Version = Math.Max(1, message.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ConversationMessages (
                CustomerId, OrderId, DealId, Direction, Channel, SenderName, Content, MessageTime, SourceMessageId, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $orderId, $dealId, $direction, $channel, $senderName, $content, $messageTime, $sourceMessageId, $metadataJson,
                $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, message);
        message.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return await GetByIdAsync(message.Id, cancellationToken) ?? message;
    }

    public async Task UpdateAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        message.UpdatedAt = DateTime.Now;
        message.IsSynced = false;
        message.Version = Math.Max(1, message.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ConversationMessages
            SET CustomerId = $customerId,
                OrderId = $orderId,
                DealId = $dealId,
                Direction = $direction,
                Channel = $channel,
                SenderName = $senderName,
                Content = $content,
                MessageTime = $messageTime,
                SourceMessageId = $sourceMessageId,
                MetadataJson = $metadataJson,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, message);
        command.Parameters.AddWithValue("$id", message.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ConversationMessage?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, CustomerId, OrderId, DealId, Direction, Channel, SenderName, Content, MessageTime, SourceMessageId, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM ConversationMessages
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<ConversationMessage?> GetBySourceMessageIdAsync(string sourceMessageId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, CustomerId, OrderId, DealId, Direction, Channel, SenderName, Content, MessageTime, SourceMessageId, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM ConversationMessages
            WHERE SourceMessageId = $sourceMessageId AND DeletedAt IS NULL
            ORDER BY Id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sourceMessageId", sourceMessageId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public Task<IReadOnlyList<ConversationMessage>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return QueryAsync(
            "CustomerId = $customerId",
            cancellationToken,
            command => command.Parameters.AddWithValue("$customerId", customerId));
    }

    public Task<IReadOnlyList<ConversationMessage>> ListAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync("1 = 1", cancellationToken);
    }

    public Task<IReadOnlyList<ConversationMessage>> ListByOrderIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return QueryAsync(
            "OrderId = $orderId",
            cancellationToken,
            command => command.Parameters.AddWithValue("$orderId", orderId));
    }

    private async Task<IReadOnlyList<ConversationMessage>> QueryAsync(
        string whereClause,
        CancellationToken cancellationToken,
        Action<SqliteCommand>? configure = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                Id, CustomerId, OrderId, DealId, Direction, Channel, SenderName, Content, MessageTime, SourceMessageId, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM ConversationMessages
            WHERE DeletedAt IS NULL AND {whereClause}
            ORDER BY MessageTime DESC, Id DESC;
            """;
        configure?.Invoke(command);

        var rows = new List<ConversationMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private static void AddParameters(SqliteCommand command, ConversationMessage message)
    {
        command.Parameters.AddWithValue("$customerId", message.CustomerId);
        command.Parameters.AddWithValue("$orderId", ToDbInt(message.OrderId));
        command.Parameters.AddWithValue("$dealId", ToDbInt(message.DealId));
        command.Parameters.AddWithValue("$direction", (int)message.Direction);
        command.Parameters.AddWithValue("$channel", (int)message.Channel);
        command.Parameters.AddWithValue("$senderName", message.SenderName);
        command.Parameters.AddWithValue("$content", message.Content);
        command.Parameters.AddWithValue("$messageTime", message.MessageTime.ToString("O"));
        command.Parameters.AddWithValue("$sourceMessageId", message.SourceMessageId);
        command.Parameters.AddWithValue("$metadataJson", message.MetadataJson);
        command.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", message.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(message.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", message.RemoteId);
        command.Parameters.AddWithValue("$isSynced", message.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", message.Version);
    }

    private static ConversationMessage Map(SqliteDataReader reader)
    {
        return new ConversationMessage
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            OrderId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            DealId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Direction = (MessageDirection)reader.GetInt32(4),
            Channel = (MessageChannel)reader.GetInt32(5),
            SenderName = reader.GetString(6),
            Content = reader.GetString(7),
            MessageTime = DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind),
            SourceMessageId = reader.GetString(9),
            MetadataJson = reader.GetString(10),
            CreatedAt = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(12), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(14),
            IsSynced = reader.GetInt32(15) == 1,
            Version = reader.GetInt32(16)
        };
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
