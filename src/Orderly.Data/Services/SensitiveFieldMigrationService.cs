using Microsoft.Data.Sqlite;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed class SensitiveFieldMigrationService
{
    private static readonly HashSet<string> AllowedBackfillTables = new(StringComparer.Ordinal)
    {
        "ActivityLogs",
        "AiSuggestions",
        "ConversationMessages",
        "CustomerNotes",
        "Customers",
        "Deals",
        "FollowUps",
        "OcrResults",
        "Orders",
        "PriceAdjustments",
        "ReplyTemplates",
        "SyncRecords"
    };

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public SensitiveFieldMigrationService(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService;
    }

    public async Task BackfillAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await BackfillTextColumnAsync(connection, transaction, "Customers", "Name", "NameCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Customers", "ContactHandle", "ContactHandleCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Customers", "Phone", "PhoneCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Customers", "Remark", "RemarkCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Customers", "ExternalId", "ExternalIdCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Customers", "RawPayload", "RawPayloadCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Customers", "LastContactAt", "LastContactAtCiphertext", "1=1", DBNull.Value, treatDbNullAsEmpty: true, cancellationToken);

        await BackfillTextColumnAsync(connection, transaction, "Deals", "Title", "TitleCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillRealColumnAsync(connection, transaction, "Deals", "EstimatedAmount", "EstimatedAmountCiphertext", "1=1", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Deals", "Requirement", "RequirementCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Deals", "ExpectedCloseAt", "ExpectedCloseAtCiphertext", "1=1", DBNull.Value, treatDbNullAsEmpty: true, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Deals", "ClosedAt", "ClosedAtCiphertext", "1=1", DBNull.Value, treatDbNullAsEmpty: true, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Deals", "LostReason", "LostReasonCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);

        await BackfillTextColumnAsync(connection, transaction, "Orders", "Title", "TitleCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillRealColumnAsync(connection, transaction, "Orders", "Amount", "AmountCiphertext", "1=1", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Orders", "Requirement", "RequirementCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Orders", "ExternalId", "ExternalIdCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Orders", "RawPayload", "RawPayloadCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "Orders", "NextFollowUpAt", "NextFollowUpAtCiphertext", "1=1", DBNull.Value, treatDbNullAsEmpty: true, cancellationToken);

        await BackfillTextColumnAsync(connection, transaction, "FollowUps", "Title", "TitleCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "FollowUps", "Content", "ContentCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "FollowUps", "ScheduledAt", "ScheduledAtCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "FollowUps", "CompletedAt", "CompletedAtCiphertext", "1=1", DBNull.Value, treatDbNullAsEmpty: true, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "FollowUps", "ReminderAt", "ReminderAtCiphertext", "1=1", DBNull.Value, treatDbNullAsEmpty: true, cancellationToken);

        await BackfillTextColumnAsync(connection, transaction, "CustomerNotes", "Content", "ContentCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);

        await BackfillTextColumnAsync(connection, transaction, "ActivityLogs", "Title", "TitleCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "ActivityLogs", "Description", "DescriptionCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "ActivityLogs", "Operator", "OperatorCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "ActivityLogs", "MetadataJson", "MetadataJsonCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);

        await BackfillTextColumnAsync(connection, transaction, "ConversationMessages", "SenderName", "SenderNameCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "ConversationMessages", "Content", "ContentCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "ConversationMessages", "MessageTime", "MessageTimeCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "ConversationMessages", "SourceMessageId", "SourceMessageIdCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "ConversationMessages", "MetadataJson", "MetadataJsonCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);

        await BackfillTextColumnAsync(connection, transaction, "AiSuggestions", "SuggestionText", "SuggestionTextCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "AiSuggestions", "Reason", "ReasonCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "AiSuggestions", "Confidence", "ConfidenceCiphertext", "1=1", DBNull.Value, treatDbNullAsEmpty: true, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "AiSuggestions", "MetadataJson", "MetadataJsonCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);

        await BackfillTextColumnAsync(connection, transaction, "OcrResults", "SourcePath", "SourcePathCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "OcrResults", "SourceName", "SourceNameCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "OcrResults", "ExtractedText", "ExtractedTextCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "OcrResults", "ErrorMessage", "ErrorMessageCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "OcrResults", "MetadataJson", "MetadataJsonCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);

        await BackfillRealColumnAsync(connection, transaction, "PriceAdjustments", "OriginalAmount", "OriginalAmountCiphertext", "1=1", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillRealColumnAsync(connection, transaction, "PriceAdjustments", "AdjustedAmount", "AdjustedAmountCiphertext", "1=1", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "PriceAdjustments", "Reason", "ReasonCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "PriceAdjustments", "RequestedBy", "RequestedByCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "PriceAdjustments", "ApprovedBy", "ApprovedByCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "PriceAdjustments", "ApprovedAt", "ApprovedAtCiphertext", "1=1", DBNull.Value, treatDbNullAsEmpty: true, cancellationToken);

        await BackfillTextColumnAsync(connection, transaction, "ReplyTemplates", "Content", "ContentCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);

        await BackfillTextColumnAsync(connection, transaction, "SyncRecords", "ErrorMessage", "ErrorMessageCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);
        await BackfillTextColumnAsync(connection, transaction, "SyncRecords", "MetadataJson", "MetadataJsonCiphertext", "1=1", "", treatDbNullAsEmpty: false, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task BackfillRealColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string plainColumn,
        string cipherColumn,
        string condition,
        bool treatDbNullAsEmpty,
        CancellationToken cancellationToken)
    {
        ValidateBackfillSqlInputs(table, plainColumn, cipherColumn, condition);
        var tableSql = QuoteSqlIdentifier(table);
        var plainColumnSql = QuoteSqlIdentifier(plainColumn);
        var cipherColumnSql = QuoteSqlIdentifier(cipherColumn);
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = $"""
            SELECT Id, CAST({plainColumnSql} AS TEXT), {cipherColumnSql}
            FROM {tableSql}
            WHERE ({condition});
            """;

        var rows = new List<(long Id, string Value, bool ShouldUpdate)>();
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                var plain = reader.IsDBNull(1) ? null : reader.GetString(1);
                var cipher = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                if (string.IsNullOrWhiteSpace(cipher))
                {
                    if (plain is not null)
                    {
                        rows.Add((id, plain, ShouldUpdate: true));
                    }
                    else if (treatDbNullAsEmpty)
                    {
                        rows.Add((id, string.Empty, ShouldUpdate: true));
                    }

                    continue;
                }

                var rewrap = TryDecryptForRewrap(cipher, table, cipherColumn, id, out var decrypted);
                if (rewrap)
                {
                    rows.Add((id, decrypted, ShouldUpdate: true));
                }
            }
        }

        foreach (var row in rows.Where(static row => row.ShouldUpdate))
        {
            var cipher = _fieldEncryptionService.Encrypt(row.Value, BuildAssociatedDataName(table, cipherColumn, row.Id));
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"""
                UPDATE {tableSql}
                SET {cipherColumnSql} = $cipher, {plainColumnSql} = 0
                WHERE Id = $id;
                """;
            update.Parameters.AddWithValue("$cipher", cipher);
            update.Parameters.AddWithValue("$id", row.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task BackfillTextColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string plainColumn,
        string cipherColumn,
        string condition,
        object clearValue,
        bool treatDbNullAsEmpty,
        CancellationToken cancellationToken)
    {
        ValidateBackfillSqlInputs(table, plainColumn, cipherColumn, condition);
        var tableSql = QuoteSqlIdentifier(table);
        var plainColumnSql = QuoteSqlIdentifier(plainColumn);
        var cipherColumnSql = QuoteSqlIdentifier(cipherColumn);
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = $"""
            SELECT Id, CAST({plainColumnSql} AS TEXT), {cipherColumnSql}
            FROM {tableSql}
            WHERE ({condition});
            """;

        var rows = new List<(long Id, string Value, bool ShouldUpdate)>();
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                var plain = reader.IsDBNull(1) ? null : reader.GetString(1);
                var cipher = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                if (string.IsNullOrWhiteSpace(cipher))
                {
                    if (plain is not null)
                    {
                        rows.Add((id, plain, ShouldUpdate: true));
                    }
                    else if (treatDbNullAsEmpty)
                    {
                        rows.Add((id, string.Empty, ShouldUpdate: true));
                    }

                    continue;
                }

                var rewrap = TryDecryptForRewrap(cipher, table, cipherColumn, id, out var decrypted);
                if (rewrap)
                {
                    rows.Add((id, decrypted, ShouldUpdate: true));
                }
            }
        }

        foreach (var row in rows.Where(static row => row.ShouldUpdate))
        {
            var cipher = _fieldEncryptionService.Encrypt(row.Value, BuildAssociatedDataName(table, cipherColumn, row.Id));
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"""
                UPDATE {tableSql}
                SET {cipherColumnSql} = $cipher, {plainColumnSql} = $clear
                WHERE Id = $id;
                """;
            update.Parameters.AddWithValue("$cipher", cipher);
            update.Parameters.AddWithValue("$clear", clearValue);
            update.Parameters.AddWithValue("$id", row.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void ValidateBackfillSqlInputs(string table, string plainColumn, string cipherColumn, string condition)
    {
        if (!AllowedBackfillTables.Contains(table) || !IsSqlIdentifier(table))
        {
            throw new InvalidOperationException("敏感字段迁移表名不在允许列表内。");
        }

        if (!IsSqlIdentifier(plainColumn) || !IsSqlIdentifier(cipherColumn))
        {
            throw new InvalidOperationException("敏感字段迁移列名必须是安全 SQL 标识符。");
        }

        if (!string.Equals(cipherColumn, plainColumn + "Ciphertext", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("敏感字段迁移明文字段与密文字段不匹配。");
        }

        if (!string.Equals(condition, "1=1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("敏感字段迁移条件不在允许列表内。");
        }
    }

    private static bool IsSqlIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !(char.IsAsciiLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var ch = value[index];
            if (!(char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static string QuoteSqlIdentifier(string value)
    {
        if (!IsSqlIdentifier(value))
        {
            throw new InvalidOperationException("敏感字段迁移 SQL 标识符无效。");
        }

        return "\"" + value + "\"";
    }

    private bool TryDecryptForRewrap(string cipher, string table, string cipherColumn, long rowId, out string value)
    {
        var scopedAssociatedData = BuildAssociatedDataName(table, cipherColumn, rowId);
        if (_fieldEncryptionService.UsesAssociatedDataPayload(cipher))
        {
            _fieldEncryptionService.Decrypt(cipher, scopedAssociatedData);
            value = string.Empty;
            return false;
        }

        value = _fieldEncryptionService.Decrypt(cipher, BuildLegacyAssociatedDataName(table, cipherColumn));
        return true;
    }

    private static string BuildAssociatedDataName(string table, string cipherColumn, long rowId)
    {
        return EncryptedFieldScope.Build(table, cipherColumn, rowId);
    }

    private static string BuildLegacyAssociatedDataName(string table, string cipherColumn)
    {
        return EncryptedFieldScope.BuildLegacyName(table, cipherColumn);
    }
}
