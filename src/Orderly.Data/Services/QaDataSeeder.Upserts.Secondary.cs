using Microsoft.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed partial class QaDataSeeder
{
    private static async Task UpsertPriceAdjustmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaPriceAdjustment adjustment,
        int customerId,
        int orderId,
        int? dealId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            $"""
            SELECT Id
            FROM PriceAdjustments
            WHERE DeletedAt IS NULL
              AND (
                    RemoteId = $remoteId
                 OR (Reason = $reason AND {QaDataScope.BuildPriceAdjustmentSelfPredicate()})
              )
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$reason", adjustment.Reason);
                command.Parameters.AddWithValue("$remoteId", adjustment.RemoteId);
                QaDataScope.AddScopeParameters(command);
            },
            cancellationToken);

        if (existingId is int adjustmentId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE PriceAdjustments
                SET CustomerId = $customerId,
                    DealId = $dealId,
                    OrderId = $orderId,
                    OriginalAmount = $originalAmount,
                    AdjustedAmount = $adjustedAmount,
                    Reason = $reason,
                    Status = $status,
                    RequestedBy = $requestedBy,
                    ApprovedBy = $approvedBy,
                    ApprovedAt = $approvedAt,
                    UpdatedAt = $updatedAt,
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddPriceAdjustmentParameters(update, adjustment, customerId, orderId, dealId, now);
            update.Parameters.AddWithValue("$id", adjustmentId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.PriceAdjustmentsUpdated++;
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO PriceAdjustments (
                CustomerId, DealId, OrderId, OriginalAmount, AdjustedAmount, Reason, Status,
                RequestedBy, ApprovedBy, ApprovedAt, CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $dealId, $orderId, $originalAmount, $adjustedAmount, $reason, $status,
                $requestedBy, $approvedBy, $approvedAt, $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        AddPriceAdjustmentParameters(insert, adjustment, customerId, orderId, dealId, now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        result.PriceAdjustmentsInserted++;
    }

    private static async Task UpsertActivityLogAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaActivityLog activity,
        int customerId,
        int? orderId,
        int? dealId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            $"""
            SELECT Id
            FROM ActivityLogs
            WHERE DeletedAt IS NULL
              AND (
                    RemoteId = $remoteId
                 OR (Title = $title AND Description = $description AND {QaDataScope.BuildActivityLogSelfPredicate()})
              )
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$title", activity.Title);
                command.Parameters.AddWithValue("$description", activity.Description);
                command.Parameters.AddWithValue("$remoteId", activity.RemoteId);
                QaDataScope.AddScopeParameters(command);
            },
            cancellationToken);

        if (existingId is int activityId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE ActivityLogs
                SET Type = $type,
                    CustomerId = $customerId,
                    DealId = $dealId,
                    OrderId = $orderId,
                    Title = $title,
                    Description = $description,
                    Operator = $operator,
                    MetadataJson = $metadataJson,
                    UpdatedAt = $updatedAt,
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddActivityLogParameters(update, activity, customerId, orderId, dealId, now);
            update.Parameters.AddWithValue("$id", activityId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.ActivityLogsUpdated++;
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO ActivityLogs (
                Type, CustomerId, DealId, OrderId, Title, Description, Operator, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $type, $customerId, $dealId, $orderId, $title, $description, $operator, $metadataJson,
                $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        AddActivityLogParameters(insert, activity, customerId, orderId, dealId, now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        result.ActivityLogsInserted++;
    }

    private static async Task<int> UpsertConversationMessageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaConversationMessage message,
        int customerId,
        int? orderId,
        int? dealId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            $"""
            SELECT Id
            FROM ConversationMessages
            WHERE DeletedAt IS NULL
              AND (
                    RemoteId = $remoteId
                 OR SourceMessageId = $sourceMessageId
                 OR (Content = $content AND {QaDataScope.BuildConversationMessageSelfPredicate()})
              )
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$remoteId", message.RemoteId);
                command.Parameters.AddWithValue("$sourceMessageId", message.SourceMessageId);
                command.Parameters.AddWithValue("$content", message.Content);
                QaDataScope.AddScopeParameters(command);
            },
            cancellationToken);

        if (existingId is int messageId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
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
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddConversationMessageParameters(update, message, customerId, orderId, dealId, now);
            update.Parameters.AddWithValue("$id", messageId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.ConversationMessagesUpdated++;
            return messageId;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO ConversationMessages (
                CustomerId, OrderId, DealId, Direction, Channel, SenderName, Content, MessageTime, SourceMessageId, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $orderId, $dealId, $direction, $channel, $senderName, $content, $messageTime, $sourceMessageId, $metadataJson,
                $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            SELECT last_insert_rowid();
            """;
        AddConversationMessageParameters(insert, message, customerId, orderId, dealId, now);
        var insertedId = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
        result.ConversationMessagesInserted++;
        return insertedId;
    }

    private static async Task UpsertAiSuggestionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaAiSuggestion suggestion,
        int customerId,
        int? orderId,
        int? messageId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            $"""
            SELECT Id
            FROM AiSuggestions
            WHERE DeletedAt IS NULL
              AND (
                    RemoteId = $remoteId
                 OR (SuggestionText = $suggestionText AND {QaDataScope.BuildAiSuggestionSelfPredicate()})
              )
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$remoteId", suggestion.RemoteId);
                command.Parameters.AddWithValue("$suggestionText", suggestion.SuggestionText);
                QaDataScope.AddScopeParameters(command);
            },
            cancellationToken);

        if (existingId is int suggestionId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE AiSuggestions
                SET CustomerId = $customerId,
                    OrderId = $orderId,
                    MessageId = $messageId,
                    SuggestionText = $suggestionText,
                    Reason = $reason,
                    Confidence = $confidence,
                    Status = $status,
                    MetadataJson = $metadataJson,
                    UpdatedAt = $updatedAt,
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddAiSuggestionParameters(update, suggestion, customerId, orderId, messageId, now);
            update.Parameters.AddWithValue("$id", suggestionId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.AiSuggestionsUpdated++;
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO AiSuggestions (
                CustomerId, OrderId, MessageId, SuggestionText, Reason, Confidence, Status, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $orderId, $messageId, $suggestionText, $reason, $confidence, $status, $metadataJson,
                $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        AddAiSuggestionParameters(insert, suggestion, customerId, orderId, messageId, now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        result.AiSuggestionsInserted++;
    }

    private static async Task UpsertOcrResultAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaOcrResult ocrResult,
        int? customerId,
        int? orderId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            $"""
            SELECT Id
            FROM OcrResults
            WHERE DeletedAt IS NULL
              AND (
                    RemoteId = $remoteId
                 OR (SourceName = $sourceName AND {QaDataScope.BuildOcrResultSelfPredicate()})
              )
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$remoteId", ocrResult.RemoteId);
                command.Parameters.AddWithValue("$sourceName", ocrResult.SourceName);
                QaDataScope.AddScopeParameters(command);
            },
            cancellationToken);

        if (existingId is int ocrResultId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
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
                    DeletedAt = NULL,
                    RemoteId = $remoteId,
                    IsSynced = 0,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddOcrResultParameters(update, ocrResult, customerId, orderId, now);
            update.Parameters.AddWithValue("$id", ocrResultId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.OcrResultsUpdated++;
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO OcrResults (
                CustomerId, OrderId, SourcePath, SourceName, ExtractedText, Status, ErrorMessage, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, RemoteId, IsSynced, Version
            )
            VALUES (
                $customerId, $orderId, $sourcePath, $sourceName, $extractedText, $status, $errorMessage, $metadataJson,
                $createdAt, $updatedAt, NULL, $remoteId, 0, 1
            );
            """;
        AddOcrResultParameters(insert, ocrResult, customerId, orderId, now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        result.OcrResultsInserted++;
    }

    private static async Task UpsertSyncRecordAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QaSyncRecord syncRecord,
        int entityId,
        DateTime now,
        QaSeedResult result,
        CancellationToken cancellationToken)
    {
        var existingId = await GetIdAsync(
            connection,
            transaction,
            $"""
            SELECT Id
            FROM SyncRecords
            WHERE DeletedAt IS NULL
              AND (
                    (EntityType = $entityType AND EntityId = $entityId)
                 OR (MetadataJson = $metadataJson AND {QaDataScope.BuildSyncRecordSelfPredicate()})
              )
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$entityType", syncRecord.EntityType);
                command.Parameters.AddWithValue("$entityId", entityId);
                command.Parameters.AddWithValue("$metadataJson", BuildQaMetadata(syncRecord.RemoteId));
                QaDataScope.AddScopeParameters(command);
            },
            cancellationToken);

        if (existingId is int syncRecordId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE SyncRecords
                SET EntityType = $entityType,
                    EntityId = $entityId,
                    RemoteId = '',
                    SyncStatus = $syncStatus,
                    LastSyncedAt = $lastSyncedAt,
                    ErrorMessage = $errorMessage,
                    MetadataJson = $metadataJson,
                    UpdatedAt = $updatedAt,
                    DeletedAt = NULL,
                    IsSynced = $isSynced,
                    Version = CASE WHEN Version < 1 THEN 1 ELSE Version + 1 END
                WHERE Id = $id;
                """;
            AddSyncRecordParameters(update, syncRecord, entityId, now);
            update.Parameters.AddWithValue("$id", syncRecordId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            result.SyncRecordsUpdated++;
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO SyncRecords (
                EntityType, EntityId, RemoteId, SyncStatus, LastSyncedAt, ErrorMessage, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, IsSynced, Version
            )
            VALUES (
                $entityType, $entityId, '', $syncStatus, $lastSyncedAt, $errorMessage, $metadataJson,
                $createdAt, $updatedAt, NULL, $isSynced, 1
            );
            """;
        AddSyncRecordParameters(insert, syncRecord, entityId, now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        result.SyncRecordsInserted++;
    }
}
