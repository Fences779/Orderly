using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class OcrResultRepository : IOcrResultRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public OcrResultRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<OcrResult> CreateAsync(OcrResult result, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (result.CreatedAt == default)
        {
            result.CreatedAt = now;
        }

        result.UpdatedAt = now;
        result.DeletedAt = null;
        result.IsSynced = false;
        result.Version = Math.Max(1, result.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO OcrResults (
                CustomerId, OrderId, SourcePath, SourceName, ExtractedText, Status, ErrorMessage, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $orderId, $sourcePath, $sourceName, $extractedText, $status, $errorMessage, $metadataJson,
                $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, result);
        result.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return await GetByIdAsync(result.Id, cancellationToken) ?? result;
    }

    public async Task UpdateAsync(OcrResult result, CancellationToken cancellationToken = default)
    {
        result.UpdatedAt = DateTime.Now;
        result.IsSynced = false;
        result.Version = Math.Max(1, result.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OcrResults
            SET CustomerId = $customerId,
                OrderId = $orderId,
                SourcePath = $sourcePath,
                SourceName = $sourceName,
                ExtractedText = $extractedText,
                Status = $status,
                ErrorMessage = $errorMessage,
                MetadataJson = $metadataJson,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, result);
        command.Parameters.AddWithValue("$id", result.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OcrResult?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, CustomerId, OrderId, SourcePath, SourceName, ExtractedText, Status, ErrorMessage, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM OcrResults
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<OcrResult>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, CustomerId, OrderId, SourcePath, SourceName, ExtractedText, Status, ErrorMessage, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM OcrResults
            WHERE DeletedAt IS NULL AND CustomerId = $customerId
            ORDER BY CreatedAt DESC, Id DESC;
            """;
        command.Parameters.AddWithValue("$customerId", customerId);

        var rows = new List<OcrResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private static void AddParameters(SqliteCommand command, OcrResult result)
    {
        command.Parameters.AddWithValue("$customerId", ToDbInt(result.CustomerId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(result.OrderId));
        command.Parameters.AddWithValue("$sourcePath", result.SourcePath);
        command.Parameters.AddWithValue("$sourceName", result.SourceName);
        command.Parameters.AddWithValue("$extractedText", result.ExtractedText);
        command.Parameters.AddWithValue("$status", (int)result.Status);
        command.Parameters.AddWithValue("$errorMessage", result.ErrorMessage);
        command.Parameters.AddWithValue("$metadataJson", result.MetadataJson);
        command.Parameters.AddWithValue("$createdAt", result.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", result.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(result.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", result.RemoteId);
        command.Parameters.AddWithValue("$isSynced", result.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", result.Version);
    }

    private static OcrResult Map(SqliteDataReader reader)
    {
        return new OcrResult
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            OrderId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            SourcePath = reader.GetString(3),
            SourceName = reader.GetString(4),
            ExtractedText = reader.GetString(5),
            Status = (OcrStatus)reader.GetInt32(6),
            ErrorMessage = reader.GetString(7),
            MetadataJson = reader.GetString(8),
            CreatedAt = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(12),
            IsSynced = reader.GetInt32(13) == 1,
            Version = reader.GetInt32(14)
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
