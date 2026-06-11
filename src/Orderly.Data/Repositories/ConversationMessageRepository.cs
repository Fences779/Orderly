using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class ConversationMessageRepository : IConversationMessageRepository
{
    private const int MaxSenderNameCharacters = 80;
    private const int MaxContentCharacters = 8000;
    private const int MaxSourceMessageIdCharacters = 160;
    private const int MaxMetadataJsonCharacters = 4096;
    private const int MaxRemoteIdCharacters = 160;

    private static readonly DateTime MinMessageTime = new(2000, 1, 1);
    private static readonly DateTime MaxMessageTime = new(2100, 1, 1);

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public ConversationMessageRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
    }

    public async Task<ConversationMessage> CreateAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        NormalizeMessage(message);
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
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ConversationMessages (
                CustomerId, OrderId, DealId, Direction, Channel,
                SenderName, SenderNameCiphertext,
                Content, ContentCiphertext,
                MessageTime, MessageTimeCiphertext,
                SourceMessageId, SourceMessageIdCiphertext,
                MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $orderId, $dealId, $direction, $channel,
                $senderName, $senderNameCiphertext,
                $content, $contentCiphertext,
                $messageTime, $messageTimeCiphertext,
                $sourceMessageId, $sourceMessageIdCiphertext,
                $metadataJson, $metadataJsonCiphertext,
                $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, message, _fieldEncryptionService);
        message.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        await UpdateEncryptedColumnsAsync(connection, transaction, message, _fieldEncryptionService, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetByIdAsync(message.Id, cancellationToken) ?? message;
    }

    public async Task UpdateAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        NormalizeMessage(message);
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
                SenderNameCiphertext = $senderNameCiphertext,
                Content = $content,
                ContentCiphertext = $contentCiphertext,
                MessageTime = $messageTime,
                MessageTimeCiphertext = $messageTimeCiphertext,
                SourceMessageId = $sourceMessageId,
                SourceMessageIdCiphertext = $sourceMessageIdCiphertext,
                MetadataJson = $metadataJson,
                MetadataJsonCiphertext = $metadataJsonCiphertext,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, message, _fieldEncryptionService);
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
                Id, CustomerId, OrderId, DealId, Direction, Channel,
                SenderName, SenderNameCiphertext,
                Content, ContentCiphertext,
                MessageTime, MessageTimeCiphertext,
                SourceMessageId, SourceMessageIdCiphertext,
                MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM ConversationMessages
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
    }

    public async Task<ConversationMessage?> GetBySourceMessageIdAsync(string sourceMessageId, CancellationToken cancellationToken = default)
    {
        sourceMessageId = NormalizeRequiredText(
            sourceMessageId,
            MaxSourceMessageIdCharacters,
            "消息来源标识",
            allowLineBreaks: false);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, CustomerId, OrderId, DealId, Direction, Channel,
                SenderName, SenderNameCiphertext,
                Content, ContentCiphertext,
                MessageTime, MessageTimeCiphertext,
                SourceMessageId, SourceMessageIdCiphertext,
                MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM ConversationMessages
            WHERE DeletedAt IS NULL
            ORDER BY Id DESC
            LIMIT 2000;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var message = Map(reader, _fieldEncryptionService);
            if (string.Equals(message.SourceMessageId, sourceMessageId, StringComparison.Ordinal))
            {
                return message;
            }
        }

        return null;
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
                Id, CustomerId, OrderId, DealId, Direction, Channel,
                SenderName, SenderNameCiphertext,
                Content, ContentCiphertext,
                MessageTime, MessageTimeCiphertext,
                SourceMessageId, SourceMessageIdCiphertext,
                MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM ConversationMessages
            WHERE DeletedAt IS NULL AND {whereClause}
            ORDER BY CreatedAt DESC, Id DESC;
            """;
        configure?.Invoke(command);

        var rows = new List<ConversationMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private static void NormalizeMessage(ConversationMessage message)
    {
        if (message.CustomerId <= 0)
        {
            throw new InvalidOperationException("会话消息缺少有效客户。");
        }

        if (message.OrderId is <= 0)
        {
            throw new InvalidOperationException("会话消息订单标识无效。");
        }

        if (message.DealId is <= 0)
        {
            throw new InvalidOperationException("会话消息成交标识无效。");
        }

        if (!Enum.IsDefined(message.Direction))
        {
            throw new InvalidOperationException("会话消息方向无效。");
        }

        if (!Enum.IsDefined(message.Channel))
        {
            throw new InvalidOperationException("会话消息渠道无效。");
        }

        if (message.MessageTime == default)
        {
            message.MessageTime = DateTime.Now;
        }
        else if (message.MessageTime < MinMessageTime || message.MessageTime > MaxMessageTime)
        {
            throw new InvalidOperationException("会话消息时间超出允许范围。");
        }

        message.SenderName = NormalizeOptionalText(message.SenderName, MaxSenderNameCharacters, "发送人", allowLineBreaks: false);
        message.Content = NormalizeRequiredText(message.Content, MaxContentCharacters, "消息内容", allowLineBreaks: true);
        message.SourceMessageId = NormalizeOptionalText(message.SourceMessageId, MaxSourceMessageIdCharacters, "消息来源标识", allowLineBreaks: false);
        message.MetadataJson = NormalizeOptionalText(message.MetadataJson, MaxMetadataJsonCharacters, "消息元数据", allowLineBreaks: false);
        message.RemoteId = NormalizeOptionalText(message.RemoteId, MaxRemoteIdCharacters, "会话消息远端标识", allowLineBreaks: false);
    }

    private static string NormalizeRequiredText(string? value, int maxCharacters, string fieldName, bool allowLineBreaks)
    {
        var normalized = NormalizeOptionalText(value, maxCharacters, fieldName, allowLineBreaks);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName}不能为空。");
        }

        return normalized;
    }

    private static string NormalizeOptionalText(string? value, int maxCharacters, string fieldName, bool allowLineBreaks)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{fieldName}不能超过 {maxCharacters} 个字符。");
        }

        if (normalized.Any(ch => char.IsControl(ch) && !(allowLineBreaks && ch is '\r' or '\n' or '\t')))
        {
            throw new InvalidOperationException($"{fieldName}不能包含控制字符。");
        }

        return normalized;
    }

    private static void AddParameters(SqliteCommand command, ConversationMessage message, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$customerId", message.CustomerId);
        command.Parameters.AddWithValue("$orderId", ToDbInt(message.OrderId));
        command.Parameters.AddWithValue("$dealId", ToDbInt(message.DealId));
        command.Parameters.AddWithValue("$direction", (int)message.Direction);
        command.Parameters.AddWithValue("$channel", (int)message.Channel);
        command.Parameters.AddWithValue("$senderName", string.Empty);
        command.Parameters.AddWithValue("$senderNameCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, message.SenderName, "ConversationMessages.SenderNameCiphertext", message.Id));
        command.Parameters.AddWithValue("$content", string.Empty);
        command.Parameters.AddWithValue("$contentCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, message.Content, "ConversationMessages.ContentCiphertext", message.Id));
        command.Parameters.AddWithValue("$messageTime", string.Empty);
        command.Parameters.AddWithValue("$messageTimeCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, message.MessageTime.ToString("O"), "ConversationMessages.MessageTimeCiphertext", message.Id));
        command.Parameters.AddWithValue("$sourceMessageId", string.Empty);
        command.Parameters.AddWithValue("$sourceMessageIdCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, message.SourceMessageId, "ConversationMessages.SourceMessageIdCiphertext", message.Id));
        command.Parameters.AddWithValue("$metadataJson", string.Empty);
        command.Parameters.AddWithValue("$metadataJsonCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, message.MetadataJson, "ConversationMessages.MetadataJsonCiphertext", message.Id));
        command.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", message.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(message.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", message.RemoteId);
        command.Parameters.AddWithValue("$isSynced", message.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", message.Version);
    }

    private static async Task UpdateEncryptedColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ConversationMessage message,
        IFieldEncryptionService fieldEncryptionService,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ConversationMessages
            SET SenderNameCiphertext = $senderNameCiphertext,
                ContentCiphertext = $contentCiphertext,
                MessageTimeCiphertext = $messageTimeCiphertext,
                SourceMessageIdCiphertext = $sourceMessageIdCiphertext,
                MetadataJsonCiphertext = $metadataJsonCiphertext
            WHERE Id = $id;
            """;
        AddParameters(command, message, fieldEncryptionService);
        command.Parameters.AddWithValue("$id", message.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ConversationMessage Map(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService)
    {
        var senderName = EncryptedColumnReader.ReadRequiredString(reader, 7, fieldEncryptionService, "ConversationMessages.SenderNameCiphertext");
        var content = EncryptedColumnReader.ReadRequiredString(reader, 9, fieldEncryptionService, "ConversationMessages.ContentCiphertext");
        var sourceMessageId = EncryptedColumnReader.ReadRequiredString(reader, 13, fieldEncryptionService, "ConversationMessages.SourceMessageIdCiphertext");
        var metadataJson = EncryptedColumnReader.ReadRequiredString(reader, 15, fieldEncryptionService, "ConversationMessages.MetadataJsonCiphertext");
        var messageTime = EncryptedColumnReader.ReadRequiredDateTime(reader, 11, fieldEncryptionService, "ConversationMessages.MessageTimeCiphertext");

        return new ConversationMessage
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            OrderId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            DealId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Direction = (MessageDirection)reader.GetInt32(4),
            Channel = (MessageChannel)reader.GetInt32(5),
            SenderName = senderName,
            Content = content,
            MessageTime = messageTime,
            SourceMessageId = sourceMessageId,
            MetadataJson = metadataJson,
            CreatedAt = DateTime.Parse(reader.GetString(16), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(17), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(18) ? null : DateTime.Parse(reader.GetString(18), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(19),
            IsSynced = reader.GetInt32(20) == 1,
            Version = reader.GetInt32(21)
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
