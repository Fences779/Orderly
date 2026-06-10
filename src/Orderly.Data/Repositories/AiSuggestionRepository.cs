using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class AiSuggestionRepository : IAiSuggestionRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public AiSuggestionRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
    }

    public async Task<AiSuggestion> CreateAsync(AiSuggestion suggestion, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (suggestion.CreatedAt == default)
        {
            suggestion.CreatedAt = now;
        }

        suggestion.UpdatedAt = now;
        suggestion.DeletedAt = null;
        suggestion.IsSynced = false;
        suggestion.Version = Math.Max(1, suggestion.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AiSuggestions (
                CustomerId, OrderId, MessageId,
                SuggestionText, SuggestionTextCiphertext,
                Reason, ReasonCiphertext,
                Confidence, ConfidenceCiphertext,
                Status,
                MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $orderId, $messageId,
                $suggestionText, $suggestionTextCiphertext,
                $reason, $reasonCiphertext,
                $confidence, $confidenceCiphertext,
                $status,
                $metadataJson, $metadataJsonCiphertext,
                $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, suggestion, _fieldEncryptionService);
        suggestion.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return await GetByIdAsync(suggestion.Id, cancellationToken) ?? suggestion;
    }

    public async Task UpdateAsync(AiSuggestion suggestion, CancellationToken cancellationToken = default)
    {
        suggestion.UpdatedAt = DateTime.Now;
        suggestion.IsSynced = false;
        suggestion.Version = Math.Max(1, suggestion.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AiSuggestions
            SET CustomerId = $customerId,
                OrderId = $orderId,
                MessageId = $messageId,
                SuggestionText = $suggestionText,
                SuggestionTextCiphertext = $suggestionTextCiphertext,
                Reason = $reason,
                ReasonCiphertext = $reasonCiphertext,
                Confidence = $confidence,
                ConfidenceCiphertext = $confidenceCiphertext,
                Status = $status,
                MetadataJson = $metadataJson,
                MetadataJsonCiphertext = $metadataJsonCiphertext,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, suggestion, _fieldEncryptionService);
        command.Parameters.AddWithValue("$id", suggestion.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AiSuggestion?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, CustomerId, OrderId, MessageId,
                SuggestionText, SuggestionTextCiphertext,
                Reason, ReasonCiphertext,
                Confidence, ConfidenceCiphertext,
                Status,
                MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM AiSuggestions
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
    }

    public Task<IReadOnlyList<AiSuggestion>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return QueryAsync(
            "CustomerId = $customerId",
            cancellationToken,
            command => command.Parameters.AddWithValue("$customerId", customerId));
    }

    public Task<IReadOnlyList<AiSuggestion>> ListAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync("1 = 1", cancellationToken);
    }

    public Task<IReadOnlyList<AiSuggestion>> ListByOrderIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return QueryAsync(
            "OrderId = $orderId",
            cancellationToken,
            command => command.Parameters.AddWithValue("$orderId", orderId));
    }

    private async Task<IReadOnlyList<AiSuggestion>> QueryAsync(
        string whereClause,
        CancellationToken cancellationToken,
        Action<SqliteCommand>? configure = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                Id, CustomerId, OrderId, MessageId,
                SuggestionText, SuggestionTextCiphertext,
                Reason, ReasonCiphertext,
                Confidence, ConfidenceCiphertext,
                Status,
                MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM AiSuggestions
            WHERE DeletedAt IS NULL AND {whereClause}
            ORDER BY CreatedAt DESC, Id DESC;
            """;
        configure?.Invoke(command);

        var rows = new List<AiSuggestion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private static void AddParameters(SqliteCommand command, AiSuggestion suggestion, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$customerId", suggestion.CustomerId);
        command.Parameters.AddWithValue("$orderId", ToDbInt(suggestion.OrderId));
        command.Parameters.AddWithValue("$messageId", ToDbInt(suggestion.MessageId));
        command.Parameters.AddWithValue("$suggestionText", string.Empty);
        command.Parameters.AddWithValue("$suggestionTextCiphertext", fieldEncryptionService.Encrypt(suggestion.SuggestionText, "AiSuggestions.SuggestionTextCiphertext"));
        command.Parameters.AddWithValue("$reason", string.Empty);
        command.Parameters.AddWithValue("$reasonCiphertext", fieldEncryptionService.Encrypt(suggestion.Reason, "AiSuggestions.ReasonCiphertext"));
        command.Parameters.AddWithValue("$confidence", DBNull.Value);
        command.Parameters.AddWithValue("$confidenceCiphertext", fieldEncryptionService.Encrypt(suggestion.Confidence?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, "AiSuggestions.ConfidenceCiphertext"));
        command.Parameters.AddWithValue("$status", (int)suggestion.Status);
        command.Parameters.AddWithValue("$metadataJson", string.Empty);
        command.Parameters.AddWithValue("$metadataJsonCiphertext", fieldEncryptionService.Encrypt(suggestion.MetadataJson, "AiSuggestions.MetadataJsonCiphertext"));
        command.Parameters.AddWithValue("$createdAt", suggestion.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", suggestion.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(suggestion.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", suggestion.RemoteId);
        command.Parameters.AddWithValue("$isSynced", suggestion.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", suggestion.Version);
    }

    private static AiSuggestion Map(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService)
    {
        var suggestionText = EncryptedColumnReader.ReadRequiredString(reader, 5, fieldEncryptionService, "AiSuggestions.SuggestionTextCiphertext");
        var reason = EncryptedColumnReader.ReadRequiredString(reader, 7, fieldEncryptionService, "AiSuggestions.ReasonCiphertext");
        var metadataJson = EncryptedColumnReader.ReadRequiredString(reader, 12, fieldEncryptionService, "AiSuggestions.MetadataJsonCiphertext");
        var confidence = EncryptedColumnReader.ReadOptionalDouble(reader, 9, fieldEncryptionService, "AiSuggestions.ConfidenceCiphertext");

        return new AiSuggestion
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            OrderId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            MessageId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            SuggestionText = suggestionText,
            Reason = reason,
            Confidence = confidence,
            Status = (AiSuggestionStatus)reader.GetInt32(10),
            MetadataJson = metadataJson,
            CreatedAt = DateTime.Parse(reader.GetString(13), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(14), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(15) ? null : DateTime.Parse(reader.GetString(15), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(16),
            IsSynced = reader.GetInt32(17) == 1,
            Version = reader.GetInt32(18)
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
