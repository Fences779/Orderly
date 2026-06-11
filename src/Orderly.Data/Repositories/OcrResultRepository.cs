using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class OcrResultRepository : IOcrResultRepository
{
    private const int MaxSourcePathCharacters = 1024;
    private const int MaxSourceNameCharacters = 160;
    private const int MaxExtractedTextCharacters = 10000;
    private const int MaxErrorMessageCharacters = 512;
    private const int MaxMetadataJsonCharacters = 4096;
    private const int MaxRemoteIdCharacters = 160;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public OcrResultRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
    }

    public async Task<OcrResult> CreateAsync(OcrResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        NormalizeResult(result);
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
                CustomerId, OrderId,
                SourcePath, SourcePathCiphertext,
                SourceName, SourceNameCiphertext,
                ExtractedText, ExtractedTextCiphertext,
                Status,
                ErrorMessage, ErrorMessageCiphertext,
                MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $orderId,
                $sourcePath, $sourcePathCiphertext,
                $sourceName, $sourceNameCiphertext,
                $extractedText, $extractedTextCiphertext,
                $status,
                $errorMessage, $errorMessageCiphertext,
                $metadataJson, $metadataJsonCiphertext,
                $createdAt, $updatedAt, $deletedAt, $remoteId, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, result, _fieldEncryptionService);
        result.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return await GetByIdAsync(result.Id, cancellationToken) ?? result;
    }

    public async Task UpdateAsync(OcrResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        NormalizeResult(result);
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
                SourcePathCiphertext = $sourcePathCiphertext,
                SourceName = $sourceName,
                SourceNameCiphertext = $sourceNameCiphertext,
                ExtractedText = $extractedText,
                ExtractedTextCiphertext = $extractedTextCiphertext,
                Status = $status,
                ErrorMessage = $errorMessage,
                ErrorMessageCiphertext = $errorMessageCiphertext,
                MetadataJson = $metadataJson,
                MetadataJsonCiphertext = $metadataJsonCiphertext,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                RemoteId = $remoteId,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, result, _fieldEncryptionService);
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
                Id, CustomerId, OrderId,
                SourcePath, SourcePathCiphertext,
                SourceName, SourceNameCiphertext,
                ExtractedText, ExtractedTextCiphertext,
                Status,
                ErrorMessage, ErrorMessageCiphertext,
                MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM OcrResults
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
    }

    public async Task<IReadOnlyList<OcrResult>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync("CustomerId = $customerId", cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$customerId", customerId);
        });
    }

    public Task<IReadOnlyList<OcrResult>> ListAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync("1 = 1", cancellationToken);
    }

    private async Task<IReadOnlyList<OcrResult>> QueryAsync(
        string whereClause,
        CancellationToken cancellationToken,
        Action<SqliteCommand>? configure = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                Id, CustomerId, OrderId,
                SourcePath, SourcePathCiphertext,
                SourceName, SourceNameCiphertext,
                ExtractedText, ExtractedTextCiphertext,
                Status,
                ErrorMessage, ErrorMessageCiphertext,
                MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            FROM OcrResults
            WHERE DeletedAt IS NULL AND {whereClause}
            ORDER BY CreatedAt DESC, Id DESC;
            """;
        configure?.Invoke(command);

        var rows = new List<OcrResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private static void NormalizeResult(OcrResult result)
    {
        if (result.CustomerId is <= 0)
        {
            throw new InvalidOperationException("OCR 客户标识无效。");
        }

        if (result.OrderId is <= 0)
        {
            throw new InvalidOperationException("OCR 订单标识无效。");
        }

        if (!Enum.IsDefined(result.Status))
        {
            throw new InvalidOperationException("OCR 状态无效。");
        }

        result.SourcePath = NormalizeRequiredText(result.SourcePath, MaxSourcePathCharacters, "OCR 图片路径", allowLineBreaks: false);
        result.SourceName = NormalizeRequiredText(result.SourceName, MaxSourceNameCharacters, "OCR 图片文件名", allowLineBreaks: false);
        result.ExtractedText = NormalizeOptionalText(result.ExtractedText, MaxExtractedTextCharacters, "OCR 文本", allowLineBreaks: true);
        result.ErrorMessage = NormalizeOptionalText(result.ErrorMessage, MaxErrorMessageCharacters, "OCR 错误信息", allowLineBreaks: true);
        result.MetadataJson = NormalizeOptionalText(result.MetadataJson, MaxMetadataJsonCharacters, "OCR 元数据", allowLineBreaks: false);
        result.RemoteId = NormalizeOptionalText(result.RemoteId, MaxRemoteIdCharacters, "OCR 远端标识", allowLineBreaks: false);
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

    private static void AddParameters(SqliteCommand command, OcrResult result, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$customerId", ToDbInt(result.CustomerId));
        command.Parameters.AddWithValue("$orderId", ToDbInt(result.OrderId));
        command.Parameters.AddWithValue("$sourcePath", string.Empty);
        command.Parameters.AddWithValue("$sourcePathCiphertext", fieldEncryptionService.Encrypt(result.SourcePath, "OcrResults.SourcePathCiphertext"));
        command.Parameters.AddWithValue("$sourceName", string.Empty);
        command.Parameters.AddWithValue("$sourceNameCiphertext", fieldEncryptionService.Encrypt(result.SourceName, "OcrResults.SourceNameCiphertext"));
        command.Parameters.AddWithValue("$extractedText", string.Empty);
        command.Parameters.AddWithValue("$extractedTextCiphertext", fieldEncryptionService.Encrypt(result.ExtractedText, "OcrResults.ExtractedTextCiphertext"));
        command.Parameters.AddWithValue("$status", (int)result.Status);
        command.Parameters.AddWithValue("$errorMessage", string.Empty);
        command.Parameters.AddWithValue("$errorMessageCiphertext", fieldEncryptionService.Encrypt(result.ErrorMessage, "OcrResults.ErrorMessageCiphertext"));
        command.Parameters.AddWithValue("$metadataJson", string.Empty);
        command.Parameters.AddWithValue("$metadataJsonCiphertext", fieldEncryptionService.Encrypt(result.MetadataJson, "OcrResults.MetadataJsonCiphertext"));
        command.Parameters.AddWithValue("$createdAt", result.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", result.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(result.DeletedAt));
        command.Parameters.AddWithValue("$remoteId", result.RemoteId);
        command.Parameters.AddWithValue("$isSynced", result.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", result.Version);
    }

    private static OcrResult Map(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService)
    {
        var sourcePath = EncryptedColumnReader.ReadRequiredString(reader, 4, fieldEncryptionService, "OcrResults.SourcePathCiphertext");
        var sourceName = EncryptedColumnReader.ReadRequiredString(reader, 6, fieldEncryptionService, "OcrResults.SourceNameCiphertext");
        var extractedText = EncryptedColumnReader.ReadRequiredString(reader, 8, fieldEncryptionService, "OcrResults.ExtractedTextCiphertext");
        var errorMessage = EncryptedColumnReader.ReadRequiredString(reader, 11, fieldEncryptionService, "OcrResults.ErrorMessageCiphertext");
        var metadataJson = EncryptedColumnReader.ReadRequiredString(reader, 13, fieldEncryptionService, "OcrResults.MetadataJsonCiphertext");

        return new OcrResult
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            OrderId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            SourcePath = sourcePath,
            SourceName = sourceName,
            ExtractedText = extractedText,
            Status = (OcrStatus)reader.GetInt32(9),
            ErrorMessage = errorMessage,
            MetadataJson = metadataJson,
            CreatedAt = DateTime.Parse(reader.GetString(14), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(15), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(16) ? null : DateTime.Parse(reader.GetString(16), null, DateTimeStyles.RoundtripKind),
            RemoteId = reader.GetString(17),
            IsSynced = reader.GetInt32(18) == 1,
            Version = reader.GetInt32(19)
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
